namespace ForRest.Services.AI.OAuth;

/// <summary>
/// Describes an OAuth 2.0 (authorization-code + PKCE) provider endpoint set for an AI provider.
/// Endpoints/client identifiers are intentionally configurable so the MAUI host can override them
/// from settings; the static defaults capture best-known public values at time of writing.
/// </summary>
public sealed record OAuthProviderConfig
{
    #region Properties

    public required AiProviderKind Provider { get; init; }

    public required string AuthorizationEndpoint { get; init; }

    public required string TokenEndpoint { get; init; }

    public required string ClientId { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>
    /// Path appended to the loopback redirect base (e.g. <c>/callback</c>) or, for manual
    /// paste-back flows, the full redirect URI registered with the provider.
    /// </summary>
    public string RedirectPath { get; init; } = "/callback";

    /// <summary>
    /// Optional fixed redirect URI used for manual paste-back. When null, the loopback flow
    /// composes one from the bound port and <see cref="RedirectPath"/>.
    /// </summary>
    public string? FixedRedirectUri { get; init; }

    /// <summary>
    /// Optional audience/resource value forwarded to the token endpoint (some providers require it).
    /// </summary>
    public string? Audience { get; init; }

    /// <summary>Optional token revocation endpoint (used for sign-out), when the provider exposes one.</summary>
    public string? RevocationEndpoint { get; init; }

    /// <summary>
    /// Optional fixed loopback port the redirect listener must bind. Some providers register an exact
    /// redirect URI (e.g. Codex uses <c>http://localhost:1455/auth/callback</c>); when null a free
    /// port is chosen per RFC 8252.
    /// </summary>
    public int? LoopbackPort { get; init; }

    #endregion

    #region Public Methods

    public string ScopeString => string.Join(' ', Scopes);

    #endregion
}

public static class OAuthProviderDefaults
{
    #region Public Methods

    /// <summary>
    /// Returns the best-known default OAuth configuration for a provider, or null when the
    /// provider does not expose a browser-based authorization-code flow.
    /// </summary>
    public static OAuthProviderConfig? GetDefault(AiProviderKind provider)
    {
        return provider switch
        {
            AiProviderKind.Grok => CreateGrokDefault(),
            AiProviderKind.OpenAI => CreateOpenAiDefault(),
            _ => null,
        };
    }

    /// <summary>
    /// xAI / X (Grok) browser sign-in defaults, using X's OAuth 2.0 authorization-code + PKCE flow.
    /// Endpoints are confirmed from the X developer docs. The <see cref="OAuthProviderConfig.ClientId"/>
    /// is intentionally a placeholder: it REQUIRES the operator to register an App in the X Developer
    /// Portal and supply its Client ID (and to whitelist the loopback redirect URI). Confidential
    /// clients additionally send a Basic auth header with the client secret at the token endpoint.
    /// </summary>
    public static OAuthProviderConfig CreateGrokDefault()
    {
        return new()
        {
            Provider = AiProviderKind.Grok,
            AuthorizationEndpoint = "https://x.com/i/oauth2/authorize",
            TokenEndpoint = "https://api.x.com/2/oauth2/token",
            RevocationEndpoint = "https://api.x.com/2/oauth2/revoke",
            // TODO (operator): register an App at https://developer.x.com → Keys and Tokens, then set
            // this Client ID (and whitelist the loopback redirect) from settings. Sign-in cannot work
            // until this is a real, registered Client ID.
            ClientId = "REPLACE_WITH_X_APP_CLIENT_ID",
            // X uses dotted scope names; offline.access yields a refresh token.
            Scopes = ["users.read", "tweet.read", "offline.access"],
            RedirectPath = "/callback",
        };
    }

    /// <summary>
    /// OpenAI (Codex / "Sign in with ChatGPT") browser sign-in defaults. Per the Codex docs this is an
    /// authorization-code + PKCE flow against auth.openai.com that returns the token to a fixed
    /// localhost:1455 loopback callback. These reuse the open-source Codex CLI's PUBLIC client and
    /// callback, so no operator app registration is required — but they are the Codex CLI's values and
    /// may change; allow overrides from settings. Note ChatGPT-login usage follows the user's ChatGPT
    /// workspace data/retention policies.
    /// </summary>
    public static OAuthProviderConfig CreateOpenAiDefault()
    {
        return new()
        {
            Provider = AiProviderKind.OpenAI,
            AuthorizationEndpoint = "https://auth.openai.com/oauth/authorize",
            TokenEndpoint = "https://auth.openai.com/oauth/token",
            // Public client used by the open-source Codex CLI (no operator registration needed).
            ClientId = "app_EMoamEEZ73f0CkXaXp7hrann",
            Scopes = ["openid", "profile", "email", "offline_access"],
            RedirectPath = "/auth/callback",
            FixedRedirectUri = "http://localhost:1455/auth/callback",
            LoopbackPort = 1455,
        };
    }

    #endregion
}
