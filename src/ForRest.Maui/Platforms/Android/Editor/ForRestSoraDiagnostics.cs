using System.Diagnostics;
using System.Text.Json;
using IO.Github.Rosemoe.Sora.Lang.Diagnostic;
using IO.Github.Rosemoe.Sora.Widget;

namespace ForRest.Maui.Platforms.Android.Editor;

/// <summary>
/// Translates the workbench diagnostics JSON (the same payload the desktop Monaco surface renders as
/// model markers) into Sora <see cref="DiagnosticsContainer"/> regions so squiggles appear under the
/// reported ranges. Defensive throughout: malformed payloads simply clear diagnostics.
/// </summary>
internal static class ForRestSoraDiagnostics
{
    #region Public Methods

    public static void Apply(CodeEditor editor, string diagnosticsJson)
    {
        try
        {
            DiagnosticsContainer container = new();
            foreach (DiagnosticItem item in Parse(diagnosticsJson))
            {
                int startIndex = ToCharIndex(editor, item.StartLineNumber, item.StartColumn);
                int endIndex = ToCharIndex(editor, item.EndLineNumber, item.EndColumn);
                if (startIndex < 0 || endIndex < 0 || endIndex < startIndex)
                {
                    continue;
                }

                if (endIndex == startIndex)
                {
                    endIndex = startIndex + 1;
                }

                short severity = string.Equals(item.Severity, "warning", StringComparison.OrdinalIgnoreCase)
                    ? DiagnosticRegion.SeverityWarning
                    : DiagnosticRegion.SeverityError;
                container.AddDiagnostic(new DiagnosticRegion(startIndex, endIndex, severity));
            }

            editor.Diagnostics = container;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraDiagnostics] Failed to apply diagnostics.{Environment.NewLine}{exception}");
        }
    }

    #endregion

    #region Private Methods

    private static IEnumerable<DiagnosticItem> Parse(string diagnosticsJson)
    {
        if (string.IsNullOrWhiteSpace(diagnosticsJson))
        {
            yield break;
        }

        List<DiagnosticItem>? items = null;
        try
        {
            items = JsonSerializer.Deserialize<List<DiagnosticItem>>(diagnosticsJson, SerializerOptions);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraDiagnostics] Failed to parse diagnostics payload.{Environment.NewLine}{exception}");
        }

        if (items is null)
        {
            yield break;
        }

        foreach (DiagnosticItem item in items)
        {
            if (item.StartLineNumber > 0)
            {
                yield return item;
            }
        }
    }

    private static int ToCharIndex(CodeEditor editor, int lineNumber, int column)
    {
        try
        {
            int line = Math.Max(0, lineNumber - 1);
            int lineCount = editor.LineCount;
            if (line >= lineCount)
            {
                line = Math.Max(0, lineCount - 1);
            }

            int maxColumn = editor.Text?.GetColumnCount(line) ?? 0;
            int col = Math.Clamp(column - 1, 0, Math.Max(0, maxColumn));
            return editor.Text?.GetCharIndex(line, col) ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    #endregion

    #region Nested Types

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private sealed class DiagnosticItem
    {
        public int StartLineNumber { get; set; }

        public int StartColumn { get; set; }

        public int EndLineNumber { get; set; }

        public int EndColumn { get; set; }

        public string? Message { get; set; }

        public string? Severity { get; set; }
    }

    #endregion
}
