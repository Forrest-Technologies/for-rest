namespace ForRest.Services.AI.OAuth;

/// <summary>
/// Pure helper that decides whether a WebView navigation target is the OAuth redirect we are waiting
/// for. The in-app browser broker intercepts navigation by comparing scheme + host + port + path while
/// ignoring the query string (the authorization code and state live in that query). Kept free of any
/// MAUI/WebView types so it can be unit-tested without a UI host.
/// </summary>
public static class RedirectUriMatcher
{
    #region Public Methods

    /// <summary>
    /// Returns true when <paramref name="navigatedUrl"/> targets the same scheme, host, port and path as
    /// <paramref name="redirectUri"/>, regardless of query or fragment. Both values must be absolute URIs.
    /// </summary>
    public static bool IsRedirect(string? navigatedUrl, string? redirectUri)
    {
        if (string.IsNullOrWhiteSpace(navigatedUrl) || string.IsNullOrWhiteSpace(redirectUri))
        {
            return false;
        }

        if (!Uri.TryCreate(navigatedUrl.Trim(), UriKind.Absolute, out Uri? navigated)
            || !Uri.TryCreate(redirectUri.Trim(), UriKind.Absolute, out Uri? target))
        {
            return false;
        }

        return string.Equals(navigated.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(navigated.Host, target.Host, StringComparison.OrdinalIgnoreCase)
            && navigated.Port == target.Port
            && string.Equals(
                NormalizePath(navigated.AbsolutePath),
                NormalizePath(target.AbsolutePath),
                StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Private Methods

    private static string NormalizePath(string path)
    {
        // Treat "/callback" and "/callback/" as the same endpoint; an empty path matches the root.
        string trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    #endregion
}
