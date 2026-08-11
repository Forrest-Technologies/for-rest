using System.Text.Json;

namespace ForRest.Services.Sharing;

/// <summary>
/// Best-effort detection of what kind of importable content a piece of pasted
/// text or a dropped file contains, so the UI can route it to the right importer.
/// </summary>
public static class ImportContentClassifier
{
    #region Public Types

    public enum ContentKind
    {
        Unknown,
        Frs,
        CurlCommand,
        PostmanCollection,
        OpenApiSpec,
        WorkspaceArchiveZip,
    }

    #endregion

    #region Public Methods

    public static ContentKind ClassifyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ContentKind.Unknown;
        }

        string trimmed = text.Trim();
        if (LooksLikeCurl(trimmed))
        {
            return ContentKind.CurlCommand;
        }

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return ClassifyJson(trimmed);
        }

        return LooksLikeFrs(trimmed) ? ContentKind.Frs : ContentKind.Unknown;
    }

    public static ContentKind ClassifyBytes(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return ContentKind.Unknown;
        }

        if (bytes.Length >= 2 && bytes[0] == 0x50 && bytes[1] == 0x4B)
        {
            return ContentKind.WorkspaceArchiveZip;
        }

        try
        {
            UTF8Encoding strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return ClassifyText(strictUtf8.GetString(bytes, offset, bytes.Length - offset));
        }
        catch (DecoderFallbackException)
        {
            return ContentKind.Unknown;
        }
    }

    #endregion

    #region Private Methods

    private static bool LooksLikeCurl(string trimmed)
    {
        string candidate = trimmed;
        while (candidate.Length > 0 && (candidate[0] == '$' || candidate[0] == '>'))
        {
            candidate = candidate[1..].TrimStart();
        }

        return candidate.StartsWith("curl ", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("curl.exe ", StringComparison.OrdinalIgnoreCase);
    }

    private static ContentKind ClassifyJson(string trimmed)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ContentKind.Unknown;
            }

            if (root.TryGetProperty("info", out JsonElement info) && info.ValueKind == JsonValueKind.Object)
            {
                string schema = info.TryGetProperty("schema", out JsonElement schemaElement) && schemaElement.ValueKind == JsonValueKind.String
                    ? schemaElement.GetString() ?? string.Empty
                    : string.Empty;
                if (schema.Contains("schema.getpostman.com", StringComparison.OrdinalIgnoreCase)
                    || info.TryGetProperty("_postman_id", out _))
                {
                    return ContentKind.PostmanCollection;
                }
            }

            if (root.TryGetProperty("openapi", out _) || root.TryGetProperty("swagger", out _))
            {
                return ContentKind.OpenApiSpec;
            }

            return ContentKind.Unknown;
        }
        catch (JsonException)
        {
            return ContentKind.Unknown;
        }
    }

    private static bool LooksLikeFrs(string trimmed)
    {
        foreach (string rawLine in trimmed.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("name \"", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("url \"", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("method ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("header \"", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
