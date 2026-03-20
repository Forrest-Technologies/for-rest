using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForRest.Maui.Services;

public static class ResponsePresentationFormatter
{
    public static string FormatBody(string? body, bool prettyPrintEnabled)
    {
        string rawBody = body ?? string.Empty;
        if (!prettyPrintEnabled || string.IsNullOrWhiteSpace(rawBody))
        {
            return NormalizeDisplayText(rawBody);
        }

        try
        {
            JsonNode? parsed = JsonNode.Parse(rawBody);
            string formatted = parsed?.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }) ?? rawBody;
            return NormalizeDisplayText(formatted);
        }
        catch
        {
            return NormalizeDisplayText(rawBody);
        }
    }

    public static string NormalizeDisplayText(string? text)
    {
        return NormalizeLineEndings(text ?? string.Empty)
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }
}
