namespace ForRest.Maui.Services;

public static class ResponseFileExtensionMapper
{
    #region Public Methods

    public static string GetExtension(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return ".txt";
        }

        string normalized = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return normalized switch
        {
            "application/json" => ".json",
            "text/html" => ".html",
            "application/xml" or "text/xml" => ".xml",
            "text/plain" => ".txt",
            "text/css" => ".css",
            "application/javascript" or "text/javascript" => ".js",
            "text/csv" => ".csv",
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/svg+xml" => ".svg",
            "application/octet-stream" => ".bin",
            _ when normalized.Contains("json", StringComparison.Ordinal) => ".json",
            _ when normalized.Contains("xml", StringComparison.Ordinal) => ".xml",
            _ when normalized.Contains("html", StringComparison.Ordinal) => ".html",
            _ => ".txt"
        };
    }

    #endregion
}
