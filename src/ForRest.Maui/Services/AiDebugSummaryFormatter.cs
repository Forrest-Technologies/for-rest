using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ForRest.Maui.Services;

/// <summary>
/// Builds a compact, clipboard-friendly digest of the AI debug output.
/// Android's clipboard refuses very long payloads (in practice anything
/// north of ~64 KiB silently truncates and many in-app paste targets cap
/// far lower), so when an AI turn fails the full debug trace — which can
/// easily run past 100 KiB once it includes the full system prompt and
/// the sanitized outbound Grok body — is unusable for sharing with a
/// support engineer. The summary keeps just the model/runtime fingerprint,
/// the user's original request, the provider error body, and the
/// executor exception stack trace.
/// </summary>
public static class AiDebugSummaryFormatter
{
    #region Private Fields

    private const int MaxSummaryLength = 12 * 1024;
    private const int MaxStackTraceLength = 8 * 1024;
    private const int MaxOriginalPromptLength = 1_000;

    private static readonly string[] DropSectionTitles =
    [
        "System prompt handed to agent",
        "Outbound Grok request body (post-sanitize)",
    ];

    private static readonly string[] KeepSectionTitles =
    [
        "Runtime preparation summary",
        "Provider error body",
        "Executor exception",
    ];

    private static readonly string[] RuntimeSummaryWhitelist =
    [
        "Provider:",
        "Transport:",
        "Model:",
        "Settings issues:",
    ];

    #endregion

    #region Public Methods

    /// <summary>
    /// Build a compact digest suitable for pasting into a bug report on
    /// Android. Always returns non-null. Falls back to a length-bounded
    /// truncation of the original text when the input does not look like
    /// a structured AI debug trace.
    /// </summary>
    public static string BuildSummary(string? debugOutputText)
    {
        if (string.IsNullOrWhiteSpace(debugOutputText))
        {
            return string.Empty;
        }

        string normalized = debugOutputText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        IReadOnlyList<DebugSection> sections = ParseSections(normalized);

        if (sections.Count == 0)
        {
            return Truncate(normalized.Trim(), MaxSummaryLength);
        }

        StringBuilder builder = new();

        AppendRuntimeFingerprint(builder, sections);
        AppendOriginalRequest(builder, sections);
        AppendProviderErrorBody(builder, sections);
        AppendExecutorException(builder, sections);

        if (builder.Length == 0)
        {
            return Truncate(normalized.Trim(), MaxSummaryLength);
        }

        return Truncate(builder.ToString().TrimEnd(), MaxSummaryLength);
    }

    #endregion

    #region Private Methods

    private static void AppendRuntimeFingerprint(StringBuilder builder, IReadOnlyList<DebugSection> sections)
    {
        DebugSection? runtime = sections.FirstOrDefault(static section =>
            string.Equals(section.Title, "Runtime preparation summary", StringComparison.Ordinal));
        if (runtime is null)
        {
            return;
        }

        List<string> kept = [];
        foreach (string line in runtime.Lines)
        {
            string trimmed = line.TrimStart();
            if (RuntimeSummaryWhitelist.Any(prefix => trimmed.StartsWith(prefix, StringComparison.Ordinal)))
            {
                kept.Add(trimmed);
            }
        }

        if (kept.Count == 0)
        {
            return;
        }

        builder.AppendLine("Model info");
        foreach (string line in kept)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine();
    }

    private static void AppendOriginalRequest(StringBuilder builder, IReadOnlyList<DebugSection> sections)
    {
        string? prompt = ExtractOriginalRequest(sections);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        builder.AppendLine("Original user request");
        builder.AppendLine(Truncate(prompt!.Trim(), MaxOriginalPromptLength));
        builder.AppendLine();
    }

    private static void AppendProviderErrorBody(StringBuilder builder, IReadOnlyList<DebugSection> sections)
    {
        DebugSection? section = sections.FirstOrDefault(static section =>
            string.Equals(section.Title, "Provider error body", StringComparison.Ordinal));
        if (section is null || section.Lines.Count == 0)
        {
            return;
        }

        builder.AppendLine("Provider error body");
        foreach (string line in section.Lines)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine();
    }

    private static void AppendExecutorException(StringBuilder builder, IReadOnlyList<DebugSection> sections)
    {
        DebugSection? section = sections.FirstOrDefault(static section =>
            string.Equals(section.Title, "Executor exception", StringComparison.Ordinal));
        if (section is null || section.Lines.Count == 0)
        {
            return;
        }

        string body = string.Join('\n', section.Lines).TrimEnd();
        if (body.Length == 0)
        {
            return;
        }

        builder.AppendLine("Executor exception");
        builder.AppendLine(Truncate(body, MaxStackTraceLength));
        builder.AppendLine();
    }

    private static string? ExtractOriginalRequest(IReadOnlyList<DebugSection> sections)
    {
        // The "Original user request:" line is appended to the system
        // prompt content, so it lives inside the giant
        // "System prompt handed to agent" section. Pull just the prompt
        // text out so we can drop the rest of that section.
        DebugSection? promptSection = sections.FirstOrDefault(static section =>
            string.Equals(section.Title, "System prompt handed to agent", StringComparison.Ordinal));
        if (promptSection is null)
        {
            return null;
        }

        StringBuilder collected = new();
        bool capturing = false;
        foreach (string line in promptSection.Lines)
        {
            if (!capturing)
            {
                int marker = line.IndexOf("Original user request", StringComparison.OrdinalIgnoreCase);
                if (marker < 0)
                {
                    continue;
                }

                int colon = line.IndexOf(':', marker);
                string remainder = colon >= 0 ? line[(colon + 1)..] : string.Empty;
                if (!string.IsNullOrWhiteSpace(remainder))
                {
                    collected.AppendLine(remainder.TrimStart());
                }

                capturing = true;
                continue;
            }

            collected.AppendLine(line);
        }

        if (collected.Length == 0)
        {
            return null;
        }

        return collected.ToString().TrimEnd();
    }

    private static IReadOnlyList<DebugSection> ParseSections(string normalized)
    {
        string[] lines = normalized.Split('\n');
        List<DebugSection> sections = [];
        DebugSection? current = null;

        bool sawAnyKnownTitle = false;

        foreach (string raw in lines)
        {
            string line = raw;
            string trimmed = line.Trim();

            if (IsKnownSectionTitle(trimmed))
            {
                sawAnyKnownTitle = true;
                current = new DebugSection(trimmed);
                sections.Add(current);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            current.Lines.Add(line);
        }

        if (!sawAnyKnownTitle)
        {
            return [];
        }

        // Trim trailing empty lines from each section so the output stays
        // tight when the source had blank-line padding between sections.
        foreach (DebugSection section in sections)
        {
            while (section.Lines.Count > 0 && string.IsNullOrWhiteSpace(section.Lines[^1]))
            {
                section.Lines.RemoveAt(section.Lines.Count - 1);
            }
        }

        return sections;
    }

    private static bool IsKnownSectionTitle(string trimmed)
    {
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        foreach (string title in KeepSectionTitles)
        {
            if (string.Equals(trimmed, title, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (string title in DropSectionTitles)
        {
            if (string.Equals(trimmed, title, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].TrimEnd() + Environment.NewLine + "... [truncated]";
    }

    #endregion

    #region Helpers

    private sealed class DebugSection(string title)
    {
        public string Title { get; } = title;

        public List<string> Lines { get; } = [];
    }

    #endregion
}
