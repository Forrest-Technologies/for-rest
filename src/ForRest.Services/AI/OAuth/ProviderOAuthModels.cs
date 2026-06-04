namespace ForRest.Services.AI.OAuth;

/// <summary>
/// How the authorization <c>code</c> is returned to the app after the user signs in.
/// </summary>
public enum OAuthCompletionMode
{
    /// <summary>An <see cref="System.Net.HttpListener"/> bound to a loopback port captures the redirect.</summary>
    LoopbackCapture,

    /// <summary>The user pastes the full redirect URL (or raw code) back into the app.</summary>
    ManualPaste,
}

/// <summary>
/// Opaque, in-flight authorization session created by <c>StartAuthorization</c> and passed back to
/// <c>CompleteAuthorization</c>. Holds the PKCE verifier and the <c>state</c> used for CSRF protection.
/// </summary>
public sealed record OAuthAuthorizationSession
{
    #region Properties

    public required AiProviderKind Provider { get; init; }

    public required OAuthProviderConfig Config { get; init; }

    public required string AuthorizationUrl { get; init; }

    public required string State { get; init; }

    public required PkceCodes Pkce { get; init; }

    public required string RedirectUri { get; init; }

    public required OAuthCompletionMode CompletionMode { get; init; }

    #endregion
}

/// <summary>
/// The typed token result returned after a successful code exchange or refresh.
/// </summary>
public sealed record ProviderOAuthToken
{
    #region Properties

    public required AiProviderKind Provider { get; init; }

    public required string AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public string TokenType { get; init; } = "Bearer";

    public string? Scope { get; init; }

    public string? IdToken { get; init; }

    public DateTimeOffset ExpiresUtc { get; init; } = DateTimeOffset.MaxValue;

    #endregion

    #region Public Methods

    /// <summary>True when the token is within the refresh skew of expiry (or already expired).</summary>
    public bool IsExpired(TimeSpan skew, DateTimeOffset nowUtc) => ExpiresUtc <= nowUtc.Add(skew);

    #endregion
}
