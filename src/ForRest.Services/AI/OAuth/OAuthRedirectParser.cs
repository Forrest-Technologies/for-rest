namespace ForRest.Services.AI.OAuth;

/// <summary>
/// Outcome of parsing a redirect URL or raw code, after validating <c>state</c>.
/// </summary>
public sealed record OAuthRedirectResult(string Code, string? State);

/// <summary>
/// Pure parsing/validation helpers for OAuth redirects. Kept free of socket and HTTP concerns so
/// the loopback listener's URL handling can be unit-tested without binding a port.
/// </summary>
public static class OAuthRedirectParser
{
    #region Public Methods

    /// <summary>
    /// Parses a pasted value that is either a full redirect URL (containing <c>?code=...&amp;state=...</c>)
    /// or a bare authorization code, then validates the returned state against the expected value.
    /// Throws <see cref="OAuthAuthorizationException"/> on provider-reported errors or state mismatch.
    /// </summary>
    public static OAuthRedirectResult ParseAndValidate(string pastedValueOrCode, string expectedState)
    {
        OAuthRedirectResult parsed = Parse(pastedValueOrCode);
        ValidateState(parsed.State, expectedState);
        return parsed;
    }

    /// <summary>
    /// Parses a redirect URL or raw code without state validation. When the input is not a URL, it
    /// is treated as a bare code (state will be null).
    /// </summary>
    public static OAuthRedirectResult Parse(string pastedValueOrCode)
    {
        if (string.IsNullOrWhiteSpace(pastedValueOrCode))
        {
            throw new OAuthAuthorizationException("No authorization code or redirect URL was provided.");
        }

        string trimmed = pastedValueOrCode.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            // Treat as a raw authorization code.
            return new(trimmed, State: null);
        }

        Dictionary<string, string> query = ParseQuery(uri.Query);

        if (query.TryGetValue("error", out string? error))
        {
            string description = query.TryGetValue("error_description", out string? value) ? value : error;
            throw new OAuthAuthorizationException($"Authorization failed: {description}");
        }

        if (!query.TryGetValue("code", out string? code) || string.IsNullOrWhiteSpace(code))
        {
            throw new OAuthAuthorizationException("Redirect URL did not contain an authorization 'code'.");
        }

        query.TryGetValue("state", out string? state);
        return new(code, state);
    }

    public static void ValidateState(string? actualState, string expectedState)
    {
        if (!string.Equals(actualState, expectedState, StringComparison.Ordinal))
        {
            throw new OAuthAuthorizationException("OAuth 'state' did not match; possible CSRF or stale session.");
        }
    }

    public static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (string segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separatorIndex = segment.IndexOf('=');
            if (separatorIndex < 0)
            {
                result[WebUtility.UrlDecode(segment)] = string.Empty;
                continue;
            }

            string key = WebUtility.UrlDecode(segment[..separatorIndex]);
            string value = WebUtility.UrlDecode(segment[(separatorIndex + 1)..]);
            result[key] = value;
        }

        return result;
    }

    #endregion
}

/// <summary>
/// Raised when an OAuth authorization or token exchange fails in a way the caller should surface.
/// </summary>
public sealed class OAuthAuthorizationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
