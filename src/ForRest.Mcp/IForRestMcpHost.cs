namespace ForRest.Mcp;

/// <summary>
/// Snapshot of the active ForRest request document as the MCP server sees it.
/// Mirrors the shape of <c>AiActiveDocumentSnapshot</c> but lives in
/// ForRest.Mcp so that the MCP tools don't have to take a dependency on the
/// AI services assembly.
/// </summary>
public sealed record ForRestMcpDocumentSnapshot(
    string DocumentId,
    string Title,
    string Language,
    string SourceText,
    string? WorkspaceName = null);

public sealed record ForRestMcpUpdateResult(bool Succeeded, string? ErrorMessage)
{
    public static ForRestMcpUpdateResult Success() => new(true, null);

    public static ForRestMcpUpdateResult Failure(string message) => new(false, message);
}

public sealed record ForRestMcpWorkspaceSummary(
    string Id,
    string Name,
    IReadOnlyList<ForRestMcpScriptSummary> Scripts);

public sealed record ForRestMcpScriptSummary(
    string Name,
    string Method,
    string UrlTemplate);

/// <summary>
/// Optional bridge supplied by the desktop app so the MCP tools can reach
/// the live canvas (active document, workspace tree, send request). When no
/// host is registered, the MCP server still runs and serves the passive
/// tools (docs search, language reference, provider list, payload catalog)
/// but the canvas-aware tools will return a "not wired" error.
/// </summary>
public interface IForRestMcpHost
{
    ForRestMcpDocumentSnapshot? GetActiveDocument();

    ForRestMcpUpdateResult ReplaceActiveDocument(string newSource);

    IReadOnlyList<ForRestMcpWorkspaceSummary> ListWorkspaces();
}
