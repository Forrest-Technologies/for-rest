using System.Text.Json;

namespace ForRest.Services.Sharing;

/// <summary>
/// Small shared helpers for rendering idiomatic .frs request scripts from
/// imported sources (curl commands, Postman collections). Every emitted
/// construct matches the syntax accepted by <c>ForRestScriptParser</c>.
/// </summary>
internal static class FrsScriptWriter
{
    #region Private Fields

    private static readonly JsonSerializerOptions prettyPrintOptions = new() { WriteIndented = true };

    #endregion

    #region Public Methods

    /// <summary>Escapes a value for use inside a double-quoted .frs string literal.</summary>
    public static string Escape(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);
    }

    /// <summary>Renders a top-level <c>header "K" = "V"</c> directive.</summary>
    public static string HeaderLine(string name, string value)
    {
        return $"header \"{Escape(name)}\" = \"{Escape(value)}\"";
    }

    /// <summary>Renders an <c>auth {{ mode = basic; ... }}</c> block for HTTP basic credentials.</summary>
    public static IEnumerable<string> BasicAuthBlock(string username, string password)
    {
        return
        [
            "auth {",
            "  mode = basic",
            $"  username = \"{Escape(username)}\"",
            $"  password = \"{Escape(password)}\"",
            "}",
        ];
    }

    /// <summary>Renders an <c>auth {{ mode = bearer; token = ... }}</c> block.</summary>
    public static IEnumerable<string> BearerAuthBlock(string token)
    {
        return
        [
            "auth {",
            "  mode = bearer",
            $"  token = \"{Escape(token)}\"",
            "}",
        ];
    }

    /// <summary>Renders an <c>auth {{ mode = apikey; ... }}</c> block placing the key in a header or query parameter.</summary>
    public static IEnumerable<string> ApiKeyAuthBlock(string name, string value, bool inQuery)
    {
        return
        [
            "auth {",
            "  mode = apikey",
            $"  name = \"{Escape(name)}\"",
            $"  value = \"{Escape(value)}\"",
            $"  location = {(inQuery ? "query" : "header")}",
            "}",
        ];
    }

    /// <summary>Renders a triple-quoted body section. JSON content is pretty-printed when it is valid JSON.</summary>
    public static IEnumerable<string> BodyBlock(string content, bool asJson)
    {
        string rendered = asJson ? PrettyPrintJson(content) : content;
        List<string> lines = [$"body {(asJson ? "json" : "text")} \"\"\""];
        lines.AddRange(rendered.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
        lines.Add("\"\"\"");
        return lines;
    }

    /// <summary>Returns true when the text parses as a JSON object or array.</summary>
    public static bool LooksLikeJson(string? content)
    {
        string trimmed = (content ?? string.Empty).Trim();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    #endregion

    #region Private Methods

    private static string PrettyPrintJson(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(document.RootElement, prettyPrintOptions);
        }
        catch (JsonException)
        {
            return content;
        }
    }

    #endregion
}
