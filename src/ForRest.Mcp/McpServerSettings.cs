namespace ForRest.Mcp;

/// <summary>
/// Runtime settings that control the desktop MCP server. Parsed from the
/// `[mcp]` section in the For-Rest settings.toml file and mapped into this
/// record by the Maui settings provider.
/// </summary>
public sealed record ForRestMcpServerSettings
{
    /// <summary>
    /// When false, the server is never started. Default is false so that
    /// users opt in intentionally.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Interface to bind to. Defaults to `127.0.0.1` so the server is
    /// only reachable from the same machine. Set to `0.0.0.0` to accept
    /// connections from the LAN.
    /// </summary>
    public string BindAddress { get; init; } = "127.0.0.1";

    /// <summary>
    /// TCP port the Streamable HTTP endpoint listens on.
    /// </summary>
    public int Port { get; init; } = 7341;

    /// <summary>
    /// Optional shared secret. When set, every request must carry an
    /// `Authorization: Bearer &lt;token&gt;` header. Leaving this blank disables the
    /// check but is only recommended when the server is bound to `127.0.0.1`.
    /// </summary>
    public string AuthToken { get; init; } = string.Empty;

    /// <summary>
    /// Hard cap on simultaneous in-flight requests. Protects the desktop app
    /// from a runaway reconnect loop.
    /// </summary>
    public int MaxConcurrentSessions { get; init; } = 4;

    public bool HasAuthToken => !string.IsNullOrWhiteSpace(AuthToken);
}
