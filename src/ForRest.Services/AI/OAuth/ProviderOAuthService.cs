using System.Collections.Concurrent;
using System.Text.Json;

namespace ForRest.Services.AI.OAuth;

/// <summary>
/// Browser-based OAuth 2.0 + PKCE sign-in for AI providers (xAI/Grok, OpenAI/Codex). Mirrors the
/// token cache + refresh-skew approach of <see cref="RequestAuthenticationService"/>.
/// </summary>
public interface IProviderOAuthService
{
    /// <summary>
    /// Begins an authorization flow: generates PKCE codes and <c>state</c>, composes the authorize
    /// URL, and (for loopback capture) reserves a free port and returns the redirect URI to use.
    /// </summary>
    OAuthAuthorizationSession StartAuthorization(
        AiProviderKind provider,
        OAuthCompletionMode completionMode = OAuthCompletionMode.LoopbackCapture,
        OAuthProviderConfig? configOverride = null);

    /// <summary>Awaits the loopback redirect started by <see cref="StartAuthorization"/> and returns the code.</summary>
    Task<OAuthRedirectResult> AwaitLoopbackRedirect(OAuthAuthorizationSession session, CancellationToken cancellationToken = default);

    /// <summary>Parses a pasted redirect URL or raw code and validates <c>state</c> for the session.</summary>
    OAuthRedirectResult ParsePastedRedirect(OAuthAuthorizationSession session, string pastedValueOrCode);

    /// <summary>Exchanges the authorization code (+ PKCE verifier) for tokens and persists the result.</summary>
    Task<ProviderOAuthToken> CompleteAuthorization(OAuthAuthorizationSession session, string code, CancellationToken cancellationToken = default);

    /// <summary>Exchanges a refresh token for a fresh access token, mirroring the cache/skew approach.</summary>
    Task<ProviderOAuthToken> RefreshToken(AiProviderKind provider, ProviderOAuthToken existingToken, OAuthProviderConfig? configOverride = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a non-expired access token, refreshing via the stored refresh token when needed.
    /// Null when no usable token exists and refresh is not possible.
    /// </summary>
    Task<ProviderOAuthToken?> GetValidToken(AiProviderKind provider, OAuthProviderConfig? configOverride = null, CancellationToken cancellationToken = default);
}

public sealed class ProviderOAuthService(
    ILogger<ProviderOAuthService> logger,
    IProviderTokenStore tokenStore,
    HttpClient? tokenClient = null) : IProviderOAuthService
{
    #region Private Fields

    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(5);

    private readonly HttpClient tokenClientInstance = tokenClient ?? new HttpClient();

    private readonly ConcurrentDictionary<AiProviderKind, ProviderOAuthToken> tokenCache = new();

    #endregion

    #region Public Methods

    public OAuthAuthorizationSession StartAuthorization(
        AiProviderKind provider,
        OAuthCompletionMode completionMode = OAuthCompletionMode.LoopbackCapture,
        OAuthProviderConfig? configOverride = null)
    {
        OAuthProviderConfig config = ResolveConfig(provider, configOverride);
        PkceCodes pkce = PkceCodes.Generate();
        string state = GenerateState();

        string redirectUri = ResolveRedirectUri(config, completionMode, out int loopbackPort);

        string authorizationUrl = BuildAuthorizationUrl(config, redirectUri, state, pkce);

        logger.LogInformation(
            "Started OAuth authorization for {Provider} via {CompletionMode} (redirect {RedirectUri})",
            provider,
            completionMode,
            redirectUri);

        return new()
        {
            Provider = provider,
            Config = config,
            AuthorizationUrl = authorizationUrl,
            State = state,
            Pkce = pkce,
            RedirectUri = redirectUri,
            CompletionMode = completionMode,
        };
    }

    public async Task<OAuthRedirectResult> AwaitLoopbackRedirect(OAuthAuthorizationSession session, CancellationToken cancellationToken = default)
    {
        if (session.CompletionMode != OAuthCompletionMode.LoopbackCapture)
        {
            throw new InvalidOperationException("AwaitLoopbackRedirect requires a loopback-capture session.");
        }

        Uri redirect = new(session.RedirectUri);
        using LoopbackRedirectListener listener = new(redirect.Port, redirect.AbsolutePath, session.State);
        listener.Start();
        logger.LogInformation("Awaiting OAuth loopback redirect for {Provider} on {RedirectUri}", session.Provider, session.RedirectUri);
        return await listener.CaptureCode(cancellationToken);
    }

    public OAuthRedirectResult ParsePastedRedirect(OAuthAuthorizationSession session, string pastedValueOrCode)
    {
        OAuthRedirectResult result = OAuthRedirectParser.Parse(pastedValueOrCode);

        // A bare code carries no state to validate; a full redirect URL must match.
        if (result.State is not null)
        {
            OAuthRedirectParser.ValidateState(result.State, session.State);
        }

        return result;
    }

    public async Task<ProviderOAuthToken> CompleteAuthorization(OAuthAuthorizationSession session, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new OAuthAuthorizationException("Cannot complete authorization without an authorization code.");
        }

        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = session.Config.ClientId,
            ["redirect_uri"] = session.RedirectUri,
            ["code_verifier"] = session.Pkce.CodeVerifier,
        };

        ProviderOAuthToken token = await ExchangeToken(session.Provider, session.Config, form, cancellationToken);
        await PersistToken(session.Provider, token, cancellationToken);
        logger.LogInformation("Completed OAuth authorization for {Provider}", session.Provider);
        return token;
    }

    public async Task<ProviderOAuthToken> RefreshToken(
        AiProviderKind provider,
        ProviderOAuthToken existingToken,
        OAuthProviderConfig? configOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(existingToken.RefreshToken))
        {
            throw new OAuthAuthorizationException("Cannot refresh: no refresh_token is available for this provider.");
        }

        OAuthProviderConfig config = ResolveConfig(provider, configOverride);

        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = existingToken.RefreshToken,
            ["client_id"] = config.ClientId,
        };

        ProviderOAuthToken refreshed = await ExchangeToken(provider, config, form, cancellationToken);

        // Some providers omit refresh_token on refresh; carry the prior one forward.
        if (string.IsNullOrWhiteSpace(refreshed.RefreshToken))
        {
            refreshed = refreshed with { RefreshToken = existingToken.RefreshToken };
        }

        await PersistToken(provider, refreshed, cancellationToken);
        logger.LogInformation("Refreshed OAuth token for {Provider}", provider);
        return refreshed;
    }

    public async Task<ProviderOAuthToken?> GetValidToken(
        AiProviderKind provider,
        OAuthProviderConfig? configOverride = null,
        CancellationToken cancellationToken = default)
    {
        ProviderOAuthToken? token = tokenCache.TryGetValue(provider, out ProviderOAuthToken? cached)
            ? cached
            : await tokenStore.Load(provider, cancellationToken);

        if (token is null)
        {
            return null;
        }

        if (!token.IsExpired(TokenRefreshSkew, DateTimeOffset.UtcNow))
        {
            tokenCache[provider] = token;
            return token;
        }

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            logger.LogWarning("Stored token for {Provider} is expired and has no refresh_token", provider);
            return null;
        }

        return await RefreshToken(provider, token, configOverride, cancellationToken);
    }

    #endregion

    #region Private Methods

    private async Task<ProviderOAuthToken> ExchangeToken(
        AiProviderKind provider,
        OAuthProviderConfig config,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(config.Audience))
        {
            form["audience"] = config.Audience;
        }

        if (config.Scopes.Count > 0 && !form.ContainsKey("scope"))
        {
            form["scope"] = config.ScopeString;
        }

        using var response = await tokenClientInstance.PostAsync(config.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Token endpoint for {Provider} returned {StatusCode}", provider, (int)response.StatusCode);
            throw new OAuthAuthorizationException($"Token endpoint returned {(int)response.StatusCode}: {payload}");
        }

        return ParseTokenResponse(provider, payload);
    }

    private static ProviderOAuthToken ParseTokenResponse(AiProviderKind provider, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("access_token", out JsonElement accessTokenElement))
        {
            throw new OAuthAuthorizationException("Token response did not contain 'access_token'.");
        }

        string accessToken = accessTokenElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new OAuthAuthorizationException("Token response returned an empty 'access_token'.");
        }

        string? refreshToken = root.TryGetProperty("refresh_token", out JsonElement refreshElement)
            ? refreshElement.GetString()
            : null;

        string tokenType = root.TryGetProperty("token_type", out JsonElement tokenTypeElement)
            && tokenTypeElement.GetString() is { Length: > 0 } parsedType
            ? parsedType
            : "Bearer";

        string? scope = root.TryGetProperty("scope", out JsonElement scopeElement) ? scopeElement.GetString() : null;
        string? idToken = root.TryGetProperty("id_token", out JsonElement idTokenElement) ? idTokenElement.GetString() : null;

        int expiresInSeconds = root.TryGetProperty("expires_in", out JsonElement expiresElement)
            && expiresElement.TryGetInt32(out int parsedSeconds)
            ? Math.Max(parsedSeconds, 60)
            : 3600;

        return new()
        {
            Provider = provider,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = tokenType,
            Scope = scope,
            IdToken = idToken,
            ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds),
        };
    }

    private async Task PersistToken(AiProviderKind provider, ProviderOAuthToken token, CancellationToken cancellationToken)
    {
        tokenCache[provider] = token;
        await tokenStore.Save(provider, token, cancellationToken);
    }

    private static OAuthProviderConfig ResolveConfig(AiProviderKind provider, OAuthProviderConfig? configOverride)
    {
        if (configOverride is not null)
        {
            return configOverride;
        }

        return OAuthProviderDefaults.GetDefault(provider)
            ?? throw new InvalidOperationException($"Provider {provider} does not have a default browser OAuth configuration; supply an explicit OAuthProviderConfig.");
    }

    private static string ResolveRedirectUri(OAuthProviderConfig config, OAuthCompletionMode completionMode, out int loopbackPort)
    {
        loopbackPort = 0;

        if (completionMode == OAuthCompletionMode.LoopbackCapture)
        {
            loopbackPort = LoopbackRedirectListener.GetFreeLoopbackPort();
            return LoopbackRedirectListener.ComposeRedirectUri(loopbackPort, config.RedirectPath);
        }

        // Manual paste-back: use the provider's registered redirect URI when supplied, else a loopback
        // placeholder so the authorize URL is still well-formed.
        return config.FixedRedirectUri ?? LoopbackRedirectListener.ComposeRedirectUri(0, config.RedirectPath);
    }

    private static string BuildAuthorizationUrl(OAuthProviderConfig config, string redirectUri, string state, PkceCodes pkce)
    {
        Dictionary<string, string> query = new(StringComparer.Ordinal)
        {
            ["response_type"] = "code",
            ["client_id"] = config.ClientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["code_challenge"] = pkce.CodeChallenge,
            ["code_challenge_method"] = PkceCodes.ChallengeMethod,
        };

        if (config.Scopes.Count > 0)
        {
            query["scope"] = config.ScopeString;
        }

        if (!string.IsNullOrWhiteSpace(config.Audience))
        {
            query["audience"] = config.Audience;
        }

        string encoded = string.Join("&", query.Select(static pair => $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}"));
        string separator = config.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        return $"{config.AuthorizationEndpoint}{separator}{encoded}";
    }

    private static string GenerateState()
    {
        return PkceCodes.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    }

    #endregion
}
