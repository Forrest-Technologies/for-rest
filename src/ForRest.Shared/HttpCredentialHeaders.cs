namespace ForRest.Shared;

/// <summary>Identifies credential-bearing HTTP headers for import and source-sharing boundaries.</summary>
public static class HttpCredentialHeaders
{
    #region Private Fields

    private static readonly string[] sensitiveHeaderNames =
    [
        "authorization", "proxy-authorization", "cookie", "set-cookie",
    ];

    private static readonly string[] sensitiveHeaderNameFragments =
    [
        "api-key", "api_key", "apikey", "token", "secret", "password", "auth", "credential",
    ];

    #endregion

    #region Public Methods

    public static bool IsSensitive(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return sensitiveHeaderNames.Any(value => string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            || sensitiveHeaderNameFragments.Any(value => name.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
