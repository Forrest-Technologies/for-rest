namespace ForRest.Mcp;

/// <summary>
/// Redacts secret-bearing values from the raw <c>settings.toml</c> before it is
/// handed to an MCP client, and restores them when the client writes the file
/// back. The settings file contains the AI provider API key, the MCP auth
/// token, and custom headers that may embed credentials, so the raw text must
/// never leave the machine verbatim.
///
/// A key is treated as secret when its name is one of the known secret keys or
/// ends in <c>_key</c> / contains <c>secret</c>, <c>password</c>, or
/// <c>token</c>. Redacted values become <c>"***"</c>. On restore, a value that
/// is still the marker is swapped back to the original (so round-tripping never
/// wipes a credential), while any other value is treated as a deliberate change
/// by the client and kept.
/// </summary>
public static class McpSettingsRedactor
{
    #region Private Fields

    private const string Marker = "\"***\"";

    #endregion

    #region Public Methods

    public static string Redact(string? settingsText)
    {
        if (string.IsNullOrEmpty(settingsText))
        {
            return settingsText ?? string.Empty;
        }

        string[] lines = settingsText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (!TrySplit(lines[index], out string left, out string key, out string value))
            {
                continue;
            }

            if (!IsSecretKey(key) || IsEmptyValue(value))
            {
                continue;
            }

            lines[index] = $"{left.TrimEnd()} = {Marker}";
        }

        return string.Join("\n", lines);
    }

    public static string Restore(string? originalText, string? updatedText)
    {
        Dictionary<string, string> originals = new(StringComparer.OrdinalIgnoreCase);
        string section = string.Empty;
        foreach (string line in (originalText ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (TryReadSection(line, out string parsedSection))
            {
                section = parsedSection;
                continue;
            }

            if (TrySplit(line, out _, out string key, out string value) && IsSecretKey(key))
            {
                originals[$"{section}|{key.ToLowerInvariant()}"] = value;
            }
        }

        if (originals.Count == 0)
        {
            return updatedText ?? string.Empty;
        }

        string[] lines = (updatedText ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        section = string.Empty;
        for (int index = 0; index < lines.Length; index++)
        {
            if (TryReadSection(lines[index], out string parsedSection))
            {
                section = parsedSection;
                continue;
            }

            if (!TrySplit(lines[index], out string left, out string key, out string value) || !IsSecretKey(key))
            {
                continue;
            }

            // Only restore when the client echoed the redaction marker back. A
            // different value means the client intentionally set a new secret.
            if (!string.Equals(value.Trim(), Marker, StringComparison.Ordinal))
            {
                continue;
            }

            if (originals.TryGetValue($"{section}|{key.ToLowerInvariant()}", out string? original))
            {
                lines[index] = $"{left.TrimEnd()} = {original}";
            }
        }

        return string.Join("\n", lines);
    }

    public static bool IsSecretKey(string key)
    {
        string normalized = key.Trim().ToLowerInvariant();
        return normalized is "custom_headers" or "api_key" or "auth_token"
                or "password" or "secret" or "token" or "client_secret"
            || normalized.EndsWith("_key", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal);
    }

    #endregion

    #region Private Methods

    private static bool TrySplit(string line, out string left, out string key, out string value)
    {
        left = string.Empty;
        key = string.Empty;
        value = string.Empty;

        string trimmedStart = line.TrimStart();
        if (trimmedStart.StartsWith('#') || trimmedStart.StartsWith('[') || trimmedStart.Length == 0)
        {
            return false;
        }

        int equals = line.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0)
        {
            return false;
        }

        left = line[..equals];
        key = left.Trim();
        value = line[(equals + 1)..].Trim();
        return key.Length > 0;
    }

    private static bool TryReadSection(string line, out string section)
    {
        section = string.Empty;
        string trimmed = line.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            section = trimmed;
            return true;
        }

        return false;
    }

    private static bool IsEmptyValue(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 || trimmed is "\"\"" or "''";
    }

    #endregion
}
