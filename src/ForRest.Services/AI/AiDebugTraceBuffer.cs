namespace ForRest.Services.AI;

public sealed class AiDebugTraceBuffer
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];

    public void AddLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        string normalized = NormalizeLineEndings(line.TrimEnd());
        lock (_gate)
        {
            _lines.Add(normalized);
        }

        System.Diagnostics.Debug.WriteLine($"[InlineAI.Trace] {normalized}");
    }

    public void AddSection(string title, string? content)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        string normalizedTitle = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
        string normalizedContent = string.IsNullOrWhiteSpace(content)
            ? string.Empty
            : NormalizeLineEndings(content.TrimEnd());
        lock (_gate)
        {
            if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1]))
            {
                _lines.Add(string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(normalizedTitle))
            {
                _lines.Add(normalizedTitle);
            }

            if (!string.IsNullOrWhiteSpace(normalizedContent))
            {
                _lines.Add(normalizedContent);
            }
        }

        System.Diagnostics.Debug.WriteLine(
            string.IsNullOrWhiteSpace(normalizedContent)
                ? $"[InlineAI.Trace]{Environment.NewLine}{normalizedTitle}"
                : $"[InlineAI.Trace]{Environment.NewLine}{normalizedTitle}{Environment.NewLine}{normalizedContent}");
    }

    public string Snapshot()
    {
        lock (_gate)
        {
            return string.Join(Environment.NewLine, _lines).Trim();
        }
    }

    public static string Truncate(string? value, int maxLength = 4000)
    {
        string normalized = NormalizeLineEndings(value ?? string.Empty).Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + Environment.NewLine + "... [truncated]";
    }

    private static string NormalizeLineEndings(string value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
