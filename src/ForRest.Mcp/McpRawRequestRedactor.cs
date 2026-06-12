namespace ForRest.Mcp;

/// <summary>
/// Masks credential-bearing header values in the raw HTTP request preview before
/// MCP tools hand it to external agents (which may be cloud-hosted). The preview
/// is the text <c>RequestCompiler.BuildRawRequest</c> produces: a request line,
/// header lines, then an optional blank line followed by the body. Header names
/// are matched broadly on purpose — over-redacting an unusual header is far
/// cheaper than leaking a bearer token or API key.
/// </summary>
public static class McpRawRequestRedactor
{
    #region Private Fields

    private const string RedactedValue = "***";

    private static readonly string[] SensitiveHeaderNames =
    [
        "authorization",
        "proxy-authorization",
        "cookie",
        "set-cookie",
    ];

    private static readonly string[] SensitiveHeaderNameFragments =
    [
        "api-key",
        "apikey",
        "token",
        "secret",
        "password",
        "auth",
    ];

    #endregion

    #region Public Methods

    /// <summary>Returns the raw request text with sensitive header values replaced by <c>***</c>.</summary>
    public static string Redact(string? rawRequest)
    {
        if (string.IsNullOrEmpty(rawRequest))
        {
            return rawRequest ?? string.Empty;
        }

        string[] lines = rawRequest.Split('\n');

        // Index 0 is the request line; headers run until the first blank line, after which the body
        // starts and must be left untouched.
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (line.Length == 0)
            {
                break;
            }

            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            string name = line[..separator].Trim();
            if (!IsSensitiveHeaderName(name))
            {
                continue;
            }

            string lineEnding = lines[index].EndsWith('\r') ? "\r" : string.Empty;
            lines[index] = $"{name}: {RedactedValue}{lineEnding}";
        }

        return string.Join('\n', lines);
    }

    #endregion

    #region Private Methods

    private static bool IsSensitiveHeaderName(string name)
    {
        foreach (string sensitiveName in SensitiveHeaderNames)
        {
            if (string.Equals(name, sensitiveName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (string fragment in SensitiveHeaderNameFragments)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
