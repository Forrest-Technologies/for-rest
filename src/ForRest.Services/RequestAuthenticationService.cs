using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ForRest.Services.AI.OAuth;
using Microsoft.Identity.Client;

namespace ForRest.Services;

public interface IRequestAuthenticationService
{
    Task<AuthenticatedPreparedRequest> PrepareAsync(PreparedRequest preparedRequest, CancellationToken cancellationToken = default);
}

public sealed record AuthenticatedPreparedRequest(
    PreparedRequest Request,
    RequestTransportAuthenticationSettings Transport)
{
    /// <summary>The bearer token acquired for OAuth modes, if any, so the runner can expose it to scripts.</summary>
    public string? AcquiredAccessToken { get; init; }
}

/// <summary>Context handed to an interactive broker to complete the authorization-code redirect.</summary>
public sealed record InteractiveAuthorizationContext(
    string AuthorizationUrl,
    string RedirectUri,
    string State,
    int? LoopbackPort);

/// <summary>
/// Completes the interactive leg of the authorization-code grant: presents the authorization URL to
/// the user and returns the authorization code from the redirect. The default implementation opens the
/// system browser and captures the code on a localhost loopback; a host can supply its own (e.g. a
/// paste-the-redirect-URL broker) for headless or non-loopback redirects such as https://oauth.pstmn.io.
/// </summary>
public interface IInteractiveAuthorizationBroker
{
    Task<string> AcquireAuthorizationCode(InteractiveAuthorizationContext context, CancellationToken cancellationToken);
}

public sealed record RequestTransportAuthenticationSettings
{
    public bool UseDefaultCredentials { get; init; }

    public ICredentials? Credentials { get; init; }

    public bool PreAuthenticate { get; init; }
}

public sealed class RequestAuthenticationService(
    ILogger<RequestAuthenticationService> logger,
    HttpClient? tokenClient = null,
    IInteractiveAuthorizationBroker? authorizationBroker = null) : IRequestAuthenticationService
{
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(5);

    private readonly HttpClient _tokenClient = tokenClient ?? new HttpClient();

    private readonly IInteractiveAuthorizationBroker _authorizationBroker =
        authorizationBroker ?? new SystemBrowserLoopbackBroker(logger);

    private readonly ConcurrentDictionary<string, CachedAccessToken> _tokenCache = new(StringComparer.Ordinal);

    public async Task<AuthenticatedPreparedRequest> PrepareAsync(PreparedRequest preparedRequest, CancellationToken cancellationToken = default)
    {
        RequestAuthDefinition auth = preparedRequest.Auth;

        switch (auth.Mode)
        {
            case AuthMode.Digest or AuthMode.Ntlm or AuthMode.Negotiate:
                return new(preparedRequest, BuildCredentialTransport(preparedRequest, auth));
            case AuthMode.OAuthClientCredentials:
                return await BuildTokenResultAsync(preparedRequest, auth, () => AcquireClientCredentialsTokenAsync(auth, cancellationToken), cancellationToken);
            case AuthMode.OAuthDeviceCode:
                return await BuildTokenResultAsync(preparedRequest, auth, () => AcquireDeviceCodeTokenAsync(auth, cancellationToken), cancellationToken);
            case AuthMode.OAuthAuthorizationCode:
                return await BuildTokenResultAsync(preparedRequest, auth, () => GetOrAcquireAuthorizationCodeTokenAsync(auth, cancellationToken), cancellationToken, useCacheWrapper: false);
            case AuthMode.OAuthIntegratedWindows:
                return await BuildTokenResultAsync(preparedRequest, auth, () => AcquireIntegratedWindowsTokenAsync(auth, cancellationToken), cancellationToken);
            default:
                return new(preparedRequest, new());
        }
    }

    private async Task<AuthenticatedPreparedRequest> BuildTokenResultAsync(
        PreparedRequest preparedRequest,
        RequestAuthDefinition auth,
        Func<Task<CachedAccessToken>> tokenFactory,
        CancellationToken cancellationToken,
        bool useCacheWrapper = true)
    {
        // The authorization-code path manages its own cache (so it can refresh), so it opts out of the
        // generic cache wrapper that would otherwise double-cache and skip the refresh branch.
        CachedAccessToken token = useCacheWrapper
            ? await GetOrAcquireTokenAsync(BuildCacheKey(auth), tokenFactory, cancellationToken)
            : await tokenFactory();

        return new(ApplyToken(preparedRequest, auth, token), new())
        {
            AcquiredAccessToken = token.AccessToken,
        };
    }

    private async Task<CachedAccessToken> GetOrAcquireTokenAsync(
        string cacheKey,
        Func<Task<CachedAccessToken>> tokenFactory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_tokenCache.TryGetValue(cacheKey, out CachedAccessToken? cached)
            && cached.ExpiresUtc > DateTimeOffset.UtcNow.Add(TokenRefreshSkew))
        {
            return cached;
        }

        CachedAccessToken token = await tokenFactory();
        _tokenCache[cacheKey] = token;
        return token;
    }

    private async Task<CachedAccessToken> AcquireClientCredentialsTokenAsync(
        RequestAuthDefinition auth,
        CancellationToken cancellationToken)
    {
        string tokenUrl = ResolveTokenUrl(auth);
        if (string.IsNullOrWhiteSpace(tokenUrl))
        {
            throw new InvalidOperationException("oauth_client_credentials requires 'token_url' or 'authority'.");
        }

        if (string.IsNullOrWhiteSpace(auth.ClientId))
        {
            throw new InvalidOperationException("oauth_client_credentials requires 'client_id'.");
        }

        if (string.IsNullOrWhiteSpace(auth.ClientSecret))
        {
            throw new InvalidOperationException("oauth_client_credentials requires 'client_secret'.");
        }

        Dictionary<string, string> formFields = new(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = auth.ClientId,
            ["client_secret"] = auth.ClientSecret,
        };

        if (!string.IsNullOrWhiteSpace(auth.Scopes))
        {
            formFields["scope"] = NormalizeScopeString(auth.Scopes);
        }

        if (!string.IsNullOrWhiteSpace(auth.Resource))
        {
            formFields["resource"] = auth.Resource;
        }

        if (!string.IsNullOrWhiteSpace(auth.Audience))
        {
            formFields["audience"] = auth.Audience;
        }

        using var response = await _tokenClient.PostAsync(tokenUrl, new FormUrlEncodedContent(formFields), cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token endpoint returned {(int)response.StatusCode}: {payload}");
        }

        using JsonDocument document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("access_token", out JsonElement accessTokenElement))
        {
            throw new InvalidOperationException("Token response did not contain 'access_token'.");
        }

        string token = accessTokenElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Token response returned an empty 'access_token'.");
        }

        int expiresInSeconds = document.RootElement.TryGetProperty("expires_in", out JsonElement expiresInElement)
            && expiresInElement.TryGetInt32(out int parsedSeconds)
            ? Math.Max(parsedSeconds, 300)
            : 3600;

        return new(token, DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds));
    }

    private async Task<CachedAccessToken> AcquireDeviceCodeTokenAsync(
        RequestAuthDefinition auth,
        CancellationToken cancellationToken)
    {
        string[] scopes = ParseScopes(auth.Scopes);
        if (scopes.Length == 0)
        {
            throw new InvalidOperationException("oauth_device_code requires 'scopes'.");
        }

        IPublicClientApplication app = PublicClientApplicationBuilder
            .Create(RequireValue(auth.ClientId, "oauth_device_code requires 'client_id'."))
            .WithAuthority(RequireValue(auth.Authority, "oauth_device_code requires 'authority'."))
            .Build();

        AuthenticationResult result = await app
            .AcquireTokenWithDeviceCode(
                scopes,
                codeResult =>
                {
                    logger.LogInformation("Device code authentication message: {Message}", codeResult.Message);
                    return Task.CompletedTask;
                })
            .ExecuteAsync(cancellationToken);

        return new(result.AccessToken, result.ExpiresOn);
    }

    private async Task<CachedAccessToken> AcquireIntegratedWindowsTokenAsync(
        RequestAuthDefinition auth,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("oauth_integrated_windows is only supported on Windows.");
        }

        string[] scopes = ParseScopes(auth.Scopes);
        if (scopes.Length == 0)
        {
            throw new InvalidOperationException("oauth_integrated_windows requires 'scopes'.");
        }

        IPublicClientApplication app = PublicClientApplicationBuilder
            .Create(RequireValue(auth.ClientId, "oauth_integrated_windows requires 'client_id'."))
            .WithAuthority(RequireValue(auth.Authority, "oauth_integrated_windows requires 'authority'."))
            .Build();

        // MSAL now prefers WAM for OS-level SSO, but IWA remains the most direct
        // compatibility path for explicit Windows pass-through in this request runner.
#pragma warning disable CS0618
        AuthenticationResult result = await app
            .AcquireTokenByIntegratedWindowsAuth(scopes)
            .ExecuteAsync(cancellationToken);
#pragma warning restore CS0618

        return new(result.AccessToken, result.ExpiresOn);
    }

    private async Task<CachedAccessToken> GetOrAcquireAuthorizationCodeTokenAsync(
        RequestAuthDefinition auth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string cacheKey = BuildCacheKey(auth);

        if (_tokenCache.TryGetValue(cacheKey, out CachedAccessToken? cached)
            && cached.ExpiresUtc > DateTimeOffset.UtcNow.Add(TokenRefreshSkew))
        {
            return cached;
        }

        if (cached is { RefreshToken: { Length: > 0 } refreshToken })
        {
            try
            {
                CachedAccessToken refreshed = await RefreshAuthorizationCodeTokenAsync(auth, refreshToken, cancellationToken);
                _tokenCache[cacheKey] = refreshed;
                return refreshed;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Refresh of authorization_code token failed; falling back to interactive sign-in.");
            }
        }

        CachedAccessToken token = await AcquireAuthorizationCodeTokenAsync(auth, cancellationToken);
        _tokenCache[cacheKey] = token;
        return token;
    }

    private async Task<CachedAccessToken> AcquireAuthorizationCodeTokenAsync(
        RequestAuthDefinition auth,
        CancellationToken cancellationToken)
    {
        string authorizationUrl = RequireValue(auth.AuthorizationUrl, "authorization_code requires 'authorization_url'.");
        string tokenUrl = ResolveTokenUrl(auth);
        if (string.IsNullOrWhiteSpace(tokenUrl))
        {
            throw new InvalidOperationException("authorization_code requires 'token_url' or 'authority'.");
        }

        string clientId = RequireValue(auth.ClientId, "authorization_code requires 'client_id'.");
        PkceCodes? pkce = auth.UsePkce ? PkceCodes.Generate() : null;
        string state = Guid.NewGuid().ToString("N");

        int? loopbackPort = null;
        string redirectUri = auth.RedirectUri;
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            loopbackPort = LoopbackRedirectListener.GetFreeLoopbackPort();
            redirectUri = LoopbackRedirectListener.ComposeRedirectUri(loopbackPort.Value, "/callback");
        }
        else if (IsLoopbackRedirect(redirectUri))
        {
            loopbackPort = new Uri(redirectUri).Port;
        }

        string authorizeUrl = BuildAuthorizeUrl(authorizationUrl, clientId, redirectUri, auth.Scopes, state, pkce, auth.CodeChallengeMethod);
        InteractiveAuthorizationContext context = new(authorizeUrl, redirectUri, state, loopbackPort);
        string code = await _authorizationBroker.AcquireAuthorizationCode(context, cancellationToken);

        Dictionary<string, string> formFields = new(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
        };

        if (pkce is not null)
        {
            formFields["code_verifier"] = pkce.CodeVerifier;
        }

        if (!string.IsNullOrWhiteSpace(auth.ClientSecret))
        {
            formFields["client_secret"] = auth.ClientSecret;
        }

        return await ExchangeTokenAsync(tokenUrl, formFields, cancellationToken);
    }

    private async Task<CachedAccessToken> RefreshAuthorizationCodeTokenAsync(
        RequestAuthDefinition auth,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        string tokenUrl = ResolveTokenUrl(auth);
        Dictionary<string, string> formFields = new(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = RequireValue(auth.ClientId, "authorization_code requires 'client_id'."),
        };

        if (!string.IsNullOrWhiteSpace(auth.ClientSecret))
        {
            formFields["client_secret"] = auth.ClientSecret;
        }

        if (!string.IsNullOrWhiteSpace(auth.Scopes))
        {
            formFields["scope"] = NormalizeScopeString(auth.Scopes);
        }

        return await ExchangeTokenAsync(tokenUrl, formFields, cancellationToken, fallbackRefreshToken: refreshToken);
    }

    private async Task<CachedAccessToken> ExchangeTokenAsync(
        string tokenUrl,
        Dictionary<string, string> formFields,
        CancellationToken cancellationToken,
        string? fallbackRefreshToken = null)
    {
        using HttpResponseMessage response = await _tokenClient.PostAsync(tokenUrl, new FormUrlEncodedContent(formFields), cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token endpoint returned {(int)response.StatusCode}: {payload}");
        }

        using JsonDocument document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("access_token", out JsonElement accessTokenElement))
        {
            throw new InvalidOperationException("Token response did not contain 'access_token'.");
        }

        string token = accessTokenElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Token response returned an empty 'access_token'.");
        }

        int expiresInSeconds = document.RootElement.TryGetProperty("expires_in", out JsonElement expiresInElement)
            && expiresInElement.TryGetInt32(out int parsedSeconds)
            ? Math.Max(parsedSeconds, 300)
            : 3600;

        string? refreshToken = document.RootElement.TryGetProperty("refresh_token", out JsonElement refreshElement)
            ? refreshElement.GetString()
            : null;

        return new(
            token,
            DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds),
            string.IsNullOrWhiteSpace(refreshToken) ? fallbackRefreshToken : refreshToken);
    }

    private static string BuildAuthorizeUrl(
        string authorizationUrl,
        string clientId,
        string redirectUri,
        string scopes,
        string state,
        PkceCodes? pkce,
        string codeChallengeMethod)
    {
        int queryIndex = authorizationUrl.IndexOf('?', StringComparison.Ordinal);
        string baseUrl = queryIndex < 0 ? authorizationUrl : authorizationUrl[..queryIndex];

        // Preserve any query already present on the authorization_url (e.g. an Azure AD B2C policy).
        List<KeyValuePair<string, string>> pairs = queryIndex < 0
            ? []
            : ParseQueryParameters(authorizationUrl[(queryIndex + 1)..]);

        UpsertPair(pairs, "response_type", "code");
        UpsertPair(pairs, "client_id", clientId);
        UpsertPair(pairs, "redirect_uri", redirectUri);
        if (!string.IsNullOrWhiteSpace(scopes))
        {
            UpsertPair(pairs, "scope", NormalizeScopeString(scopes));
        }

        UpsertPair(pairs, "state", state);
        if (pkce is not null)
        {
            UpsertPair(pairs, "code_challenge", pkce.CodeChallenge);
            UpsertPair(pairs, "code_challenge_method", string.IsNullOrWhiteSpace(codeChallengeMethod) ? "S256" : codeChallengeMethod);
        }

        string query = string.Join("&", pairs.Select(static pair => $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}"));
        return $"{baseUrl}?{query}";
    }

    private static bool IsLoopbackRedirect(string redirectUri)
    {
        return Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? uri)
            && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
    }

    private static RequestTransportAuthenticationSettings BuildCredentialTransport(
        PreparedRequest preparedRequest,
        RequestAuthDefinition auth)
    {
        if (auth.UseDefaultCredentials)
        {
            return new()
            {
                UseDefaultCredentials = true,
                PreAuthenticate = auth.Mode == AuthMode.Digest,
            };
        }

        string username = auth.Username;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException($"{auth.Mode} authentication requires 'username' or 'use_default_credentials = true'.");
        }

        NetworkCredential credential = string.IsNullOrWhiteSpace(auth.Domain)
            ? new(username, auth.Password)
            : new(username, auth.Password, auth.Domain);

        CredentialCache cache = new();
        cache.Add(
            new Uri(preparedRequest.Uri.GetLeftPart(UriPartial.Authority)),
            ResolveChallengeScheme(auth.Mode),
            credential);

        return new()
        {
            Credentials = cache,
            PreAuthenticate = auth.Mode == AuthMode.Digest,
        };
    }

    private static PreparedRequest ApplyToken(
        PreparedRequest preparedRequest,
        RequestAuthDefinition auth,
        CachedAccessToken token)
    {
        if (auth.ApiKeyLocation == ApiKeyLocation.Query)
        {
            UriBuilder uriBuilder = new(preparedRequest.Uri);
            List<KeyValuePair<string, string>> pairs = ParseQueryParameters(uriBuilder.Query);
            UpsertPair(pairs, ResolveQueryParameterName(auth), BuildAuthValue(auth, token.AccessToken, defaultScheme: "Bearer"));
            uriBuilder.Query = string.Join("&", pairs.Select(static pair => $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}"));

            PreparedRequest updated = preparedRequest with
            {
                Uri = uriBuilder.Uri,
            };

            return updated with
            {
                RawRequest = BuildRawRequest(updated),
            };
        }

        List<KeyValueDefinition> headers =
        [
            .. preparedRequest.Headers.Where(
                header => !string.Equals(header.Key, ResolveHeaderName(auth), StringComparison.OrdinalIgnoreCase))
        ];
        headers.Add(
            new()
            {
                Key = ResolveHeaderName(auth),
                Value = BuildAuthValue(auth, token.AccessToken, defaultScheme: "Bearer"),
            });

        PreparedRequest requestWithHeaders = preparedRequest with
        {
            Headers = headers,
        };

        return requestWithHeaders with
        {
            RawRequest = BuildRawRequest(requestWithHeaders),
        };
    }

    private static string ResolveChallengeScheme(AuthMode mode)
    {
        return mode switch
        {
            AuthMode.Digest => "Digest",
            AuthMode.Ntlm => "NTLM",
            AuthMode.Negotiate => "Negotiate",
            _ => string.Empty,
        };
    }

    private static string ResolveHeaderName(RequestAuthDefinition auth)
    {
        return string.IsNullOrWhiteSpace(auth.HeaderName) ? "Authorization" : auth.HeaderName;
    }

    private static string ResolveQueryParameterName(RequestAuthDefinition auth)
    {
        if (!string.IsNullOrWhiteSpace(auth.QueryParameterName))
        {
            return auth.QueryParameterName;
        }

        if (!string.IsNullOrWhiteSpace(auth.ApiKeyName))
        {
            return auth.ApiKeyName;
        }

        return "access_token";
    }

    private static string BuildAuthValue(RequestAuthDefinition auth, string value, string defaultScheme)
    {
        string scheme = string.IsNullOrWhiteSpace(auth.Scheme)
            ? ResolveDefaultTokenScheme(auth, defaultScheme)
            : auth.Scheme.Trim();
        return string.IsNullOrWhiteSpace(scheme) ? value : $"{scheme} {value}";
    }

    private static string ResolveDefaultTokenScheme(RequestAuthDefinition auth, string defaultScheme)
    {
        return auth.ApiKeyLocation == ApiKeyLocation.Header
               && string.Equals(ResolveHeaderName(auth), "Authorization", StringComparison.OrdinalIgnoreCase)
            ? defaultScheme
            : string.Empty;
    }

    private static string ResolveTokenUrl(RequestAuthDefinition auth)
    {
        if (!string.IsNullOrWhiteSpace(auth.TokenUrl))
        {
            return auth.TokenUrl;
        }

        if (string.IsNullOrWhiteSpace(auth.Authority))
        {
            return string.Empty;
        }

        return auth.Authority.TrimEnd('/') + "/oauth2/v2.0/token";
    }

    private static string[] ParseScopes(string rawScopes)
    {
        return (rawScopes ?? string.Empty)
            .Split([' ', ',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeScopeString(string rawScopes)
    {
        return string.Join(" ", ParseScopes(rawScopes));
    }

    private static string RequireValue(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }

        return value;
    }

    private static string BuildCacheKey(RequestAuthDefinition auth)
    {
        return string.Join(
            "|",
            [
                auth.Mode.ToString(),
                auth.Authority,
                auth.TokenUrl,
                auth.ClientId,
                auth.Scopes,
                auth.Resource,
                auth.Audience,
                auth.HeaderName,
                auth.QueryParameterName,
                auth.Scheme,
                auth.ApiKeyLocation.ToString()
            ]);
    }

    private static List<KeyValuePair<string, string>> ParseQueryParameters(string query)
    {
        List<KeyValuePair<string, string>> pairs = [];
        foreach (string segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separatorIndex = segment.IndexOf('=');
            if (separatorIndex < 0)
            {
                pairs.Add(KeyValuePair.Create(WebUtility.UrlDecode(segment), string.Empty));
                continue;
            }

            pairs.Add(
                KeyValuePair.Create(
                    WebUtility.UrlDecode(segment[..separatorIndex]),
                    WebUtility.UrlDecode(segment[(separatorIndex + 1)..])));
        }

        return pairs;
    }

    private static void UpsertPair(List<KeyValuePair<string, string>> pairs, string key, string value)
    {
        for (int index = pairs.Count - 1; index >= 0; index--)
        {
            if (string.Equals(pairs[index].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                pairs.RemoveAt(index);
            }
        }

        pairs.Add(KeyValuePair.Create(key, value));
    }

    private static string BuildRawRequest(PreparedRequest preparedRequest)
    {
        StringBuilder builder = new();
        builder.Append(preparedRequest.Method.ToString().ToUpperInvariant());
        builder.Append(' ');
        builder.Append(preparedRequest.Uri);
        builder.AppendLine();

        foreach (KeyValueDefinition header in preparedRequest.Headers.Where(static item => item.IsEnabled))
        {
            builder.Append(header.Key);
            builder.Append(": ");
            builder.AppendLine(header.Value);
        }

        if (preparedRequest.Body.Mode != RequestBodyMode.None)
        {
            builder.AppendLine();
            if (preparedRequest.Body.Mode is RequestBodyMode.FormUrlEncoded or RequestBodyMode.MultipartFormData)
            {
                builder.AppendLine(string.Join("&", preparedRequest.Body.FormValues.Where(static item => item.IsEnabled).Select(static item => $"{item.Key}={item.Value}")));
            }
            else
            {
                builder.AppendLine(preparedRequest.Body.RawContent);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private sealed record CachedAccessToken(string AccessToken, DateTimeOffset ExpiresUtc, string? RefreshToken = null);
}

/// <summary>
/// Default <see cref="IInteractiveAuthorizationBroker"/>: opens the system browser to the authorization
/// URL and captures the authorization code on a localhost loopback listener. A non-loopback redirect
/// (e.g. https://oauth.pstmn.io/v1/callback) requires a host-supplied broker that captures the redirect
/// some other way (such as pasting the returned URL).
/// </summary>
public sealed class SystemBrowserLoopbackBroker(ILogger logger) : IInteractiveAuthorizationBroker
{
    #region Public Methods

    public async Task<string> AcquireAuthorizationCode(InteractiveAuthorizationContext context, CancellationToken cancellationToken)
    {
        if (context.LoopbackPort is not int port)
        {
            throw new InvalidOperationException(
                "A non-loopback redirect_uri requires an interactive broker that captures the redirect "
                + "(for example a paste-the-URL flow). Register a custom IInteractiveAuthorizationBroker.");
        }

        string redirectPath = new Uri(context.RedirectUri).AbsolutePath;
        using LoopbackRedirectListener listener = new(port, redirectPath, context.State);
        listener.Start();

        OpenSystemBrowser(context.AuthorizationUrl);

        OAuthRedirectResult result = await listener.CaptureCode(cancellationToken);
        return result.Code;
    }

    #endregion

    #region Private Methods

    private void OpenSystemBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not launch the system browser. Open this URL manually to sign in: {Url}", url);
        }
    }

    #endregion
}
