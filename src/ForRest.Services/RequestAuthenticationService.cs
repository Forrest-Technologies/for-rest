using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Identity.Client;

namespace ForRest.Services;

public interface IRequestAuthenticationService
{
    Task<AuthenticatedPreparedRequest> PrepareAsync(PreparedRequest preparedRequest, CancellationToken cancellationToken = default);
}

public sealed record AuthenticatedPreparedRequest(
    PreparedRequest Request,
    RequestTransportAuthenticationSettings Transport);

public sealed record RequestTransportAuthenticationSettings
{
    public bool UseDefaultCredentials { get; init; }

    public ICredentials? Credentials { get; init; }

    public bool PreAuthenticate { get; init; }
}

public sealed class RequestAuthenticationService(
    ILogger<RequestAuthenticationService> logger,
    HttpClient? tokenClient = null) : IRequestAuthenticationService
{
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(5);

    private readonly HttpClient _tokenClient = tokenClient ?? new HttpClient();

    private readonly ConcurrentDictionary<string, CachedAccessToken> _tokenCache = new(StringComparer.Ordinal);

    public async Task<AuthenticatedPreparedRequest> PrepareAsync(PreparedRequest preparedRequest, CancellationToken cancellationToken = default)
    {
        RequestAuthDefinition auth = preparedRequest.Auth;

        return auth.Mode switch
        {
            AuthMode.Digest or AuthMode.Ntlm or AuthMode.Negotiate => new(
                preparedRequest,
                BuildCredentialTransport(preparedRequest, auth)),
            AuthMode.OAuthClientCredentials => new(
                ApplyToken(preparedRequest, auth, await GetOrAcquireTokenAsync(BuildCacheKey(auth), () => AcquireClientCredentialsTokenAsync(auth, cancellationToken), cancellationToken)),
                new()),
            AuthMode.OAuthDeviceCode => new(
                ApplyToken(preparedRequest, auth, await GetOrAcquireTokenAsync(BuildCacheKey(auth), () => AcquireDeviceCodeTokenAsync(auth, cancellationToken), cancellationToken)),
                new()),
            AuthMode.OAuthIntegratedWindows => new(
                ApplyToken(preparedRequest, auth, await GetOrAcquireTokenAsync(BuildCacheKey(auth), () => AcquireIntegratedWindowsTokenAsync(auth, cancellationToken), cancellationToken)),
                new()),
            _ => new(preparedRequest, new()),
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

    private sealed record CachedAccessToken(string AccessToken, DateTimeOffset ExpiresUtc);
}
