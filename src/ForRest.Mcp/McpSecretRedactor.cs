using System.Text.RegularExpressions;

namespace ForRest.Mcp;

/// <summary>
/// Redacts and restores <c>secret name = value</c> declarations in ForRest
/// scripts so that external MCP clients (which may be cloud-hosted agents)
/// never see raw secret values, while the desktop host still persists the
/// real declarations. Mirrors the redaction performed on the in-app AI path
/// so the two surfaces stay consistent.
///
/// Read tools call <see cref="Redact"/> before returning script source.
/// Write tools (handled by the host, which holds the original source) call
/// <see cref="Restore"/> so any <c>"***"</c> placeholder an agent copied back
/// is replaced with the original value, and any secret the agent deleted is
/// re-inserted.
/// </summary>
public static partial class McpSecretRedactor
{
    #region Private Fields

    private const string RedactedSecretMarker = "\"***\"";

    [GeneratedRegex(
        @"^(?<indent>[ \t]*)secret[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*=[ \t]*(?<value>.+?)[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SecretDeclarationPattern();

    #endregion

    #region Public Methods

    /// <summary>Replaces every secret declaration's right-hand side with <c>"***"</c>.</summary>
    public static string Redact(string? sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            return sourceText ?? string.Empty;
        }

        return SecretDeclarationPattern().Replace(sourceText, static match =>
        {
            string indent = match.Groups["indent"].Value;
            string name = match.Groups["name"].Value;
            return $"{indent}secret {name} = {RedactedSecretMarker}";
        });
    }

    /// <summary>
    /// Re-applies the original secret declarations from
    /// <paramref name="originalSourceText"/> onto <paramref name="newSourceText"/>,
    /// which an agent built from the redacted view. Surviving secret lines are
    /// restored to their original value and any deleted secret is re-inserted at
    /// the top of the document so credentials are never silently lost.
    /// </summary>
    public static string Restore(string? originalSourceText, string? newSourceText)
    {
        IReadOnlyDictionary<string, string> originals = ExtractDeclarations(originalSourceText);
        if (originals.Count == 0)
        {
            return newSourceText ?? string.Empty;
        }

        string updated = newSourceText ?? string.Empty;
        HashSet<string> seen = new(StringComparer.Ordinal);

        updated = SecretDeclarationPattern().Replace(updated, match =>
        {
            string name = match.Groups["name"].Value;
            if (!originals.TryGetValue(name, out string? originalLine))
            {
                return match.Value;
            }

            seen.Add(name);
            string indent = match.Groups["indent"].Value;
            int firstNonWhitespace = 0;
            while (firstNonWhitespace < originalLine.Length &&
                (originalLine[firstNonWhitespace] == ' ' || originalLine[firstNonWhitespace] == '\t'))
            {
                firstNonWhitespace++;
            }

            return indent + originalLine[firstNonWhitespace..];
        });

        List<string> missing = [];
        foreach (KeyValuePair<string, string> entry in originals)
        {
            if (!seen.Contains(entry.Key))
            {
                missing.Add(entry.Value);
            }
        }

        if (missing.Count > 0)
        {
            string newline = updated.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            updated = string.Join(newline, missing) + newline + updated;
        }

        return updated;
    }

    #endregion

    #region Private Methods

    private static IReadOnlyDictionary<string, string> ExtractDeclarations(string? sourceText)
    {
        Dictionary<string, string> declarations = new(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(sourceText))
        {
            return declarations;
        }

        foreach (Match match in SecretDeclarationPattern().Matches(sourceText))
        {
            string name = match.Groups["name"].Value;
            if (string.IsNullOrEmpty(name) || declarations.ContainsKey(name))
            {
                continue;
            }

            declarations[name] = match.Value.TrimEnd();
        }

        return declarations;
    }

    #endregion
}
