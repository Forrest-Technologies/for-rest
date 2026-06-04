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
    /// xAI (Grok) browser sign-in defaults.
    /// TODO: confirm against provider docs — xAI's public OAuth authorization-code + PKCE
    /// endpoints and the first-party client_id are not yet finalized publicly. These are
    /// best-known placeholders and MUST be overridden from settings before shipping.
    /// </summary>
    public static OAuthProviderConfig CreateGrokDefault()
    {
        return new()
        {
            Provider = AiProviderKind.Grok,
            // TODO: confirm against provider docs.
            AuthorizationEndpoint = "https://accounts.x.ai/oauth/authorize",
            // TODO: confirm against provider docs.
            TokenEndpoint = "https://api.x.ai/oauth/token",
            // TODO: confirm against provider docs — no public first-party desktop client_id yet.
            ClientId = "forrest-grok",
            Scopes = ["openid", "profile", "offline_access", "api"],
            RedirectPath = "/callback",
        };
    }

    /// <summary>
    /// OpenAI (Codex / ChatGPT-style) browser sign-in defaults. The Codex CLI uses an
    /// authorization-code + PKCE flow against auth.openai.com with a loopback redirect.
    /// TODO: confirm against provider docs — the first-party Codex client_id and exact scopes
    /// are subject to change; treat these as best-known defaults and allow overrides.
    /// </summary>
    public static OAuthProviderConfig CreateOpenAiDefault()
    {
        return new()
        {
            Provider = AiProviderKind.OpenAI,
            // TODO: confirm against provider docs.
            AuthorizationEndpoint = "https://auth.openai.com/oauth/authorize",
            // TODO: confirm against provider docs.
            TokenEndpoint = "https://auth.openai.com/oauth/token",
            // TODO: confirm against provider docs — Codex CLI public client identifier.
            ClientId = "app_EMoamEEZ73f0CkXaXp7hrann",
            Scopes = ["openid", "profile", "email", "offline_access"],
            RedirectPath = "/auth/callback",
        };
    }

    #endregion
}
