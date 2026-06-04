namespace ForRest.Mcp;

#region Document & mutation results

/// <summary>
/// Snapshot of a ForRest request document as the MCP server sees it. Mirrors
/// the shape of the in-app AI snapshot but lives in ForRest.Mcp so the MCP
/// tools do not take a dependency on the AI services assembly.
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

/// <summary>Generic create/update/delete outcome. <see cref="Id"/> carries the
/// new identifier (workspace id, script location) when one is produced.</summary>
public sealed record ForRestMcpMutationResult(bool Succeeded, string? Message, string? Id = null)
{
    public static ForRestMcpMutationResult Ok(string? message = null, string? id = null) => new(true, message, id);

    public static ForRestMcpMutationResult Fail(string message) => new(false, message);
}

#endregion

#region Workspace & script views

public sealed record ForRestMcpScriptSummary(
    string Name,
    string Method,
    string UrlTemplate,
    string Location = "");

public sealed record ForRestMcpWorkspaceSummary(
    string Id,
    string Name,
    IReadOnlyList<ForRestMcpScriptSummary> Scripts);

public sealed record ForRestMcpScriptDetail(
    string WorkspaceId,
    string WorkspaceName,
    string Location,
    string Name,
    string Method,
    string Summary,
    string Source,
    string PreRequestScript);

#endregion

#region Compile / execution views

public sealed record ForRestMcpDiagnostic(string Severity, string Message, int Line, int Column);

public sealed record ForRestMcpCompileResult(
    bool Succeeded,
    IReadOnlyList<ForRestMcpDiagnostic> Diagnostics,
    string? Method,
    string? Url,
    string? RequestName);

public sealed record ForRestMcpHeaderView(string Key, string Value);

public sealed record ForRestMcpResponseView(
    int Status,
    string ReasonPhrase,
    string ContentType,
    long SizeBytes,
    long DurationMilliseconds,
    IReadOnlyList<ForRestMcpHeaderView> Headers,
    IReadOnlyList<ForRestMcpHeaderView> Cookies,
    string Body,
    string RawResponse,
    string? Label);

public sealed record ForRestMcpTestView(string Name, string State, string Message);

public sealed record ForRestMcpLogView(string Level, string Message, DateTimeOffset TimestampUtc);

public sealed record ForRestMcpStashView(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

public sealed record ForRestMcpExecutionResult(
    bool Succeeded,
    string State,
    IReadOnlyList<ForRestMcpDiagnostic> Diagnostics,
    string? RunId,
    ForRestMcpResponseView? Response,
    IReadOnlyList<ForRestMcpTestView> Tests,
    IReadOnlyList<ForRestMcpLogView> Logs,
    ForRestMcpStashView? Stash,
    string? ErrorMessage);

#endregion

#region History / monitoring views

public sealed record ForRestMcpRunSummary(
    string Id,
    string RequestName,
    string State,
    int Iteration,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    string TargetUri,
    int? Status,
    long? DurationMilliseconds,
    string ErrorMessage);

public sealed record ForRestMcpRunDetail(
    ForRestMcpRunSummary Summary,
    ForRestMcpResponseView? Response,
    IReadOnlyList<ForRestMcpResponseView> Responses,
    IReadOnlyList<ForRestMcpTestView> Tests,
    IReadOnlyList<ForRestMcpLogView> Logs,
    ForRestMcpStashView? Stash,
    string RawRequest);

#endregion

#region Settings view

public sealed record ForRestMcpServerSettingsView(
    bool Enabled,
    string BindAddress,
    int Port,
    bool HasAuthToken,
    int MaxConcurrentSessions,
    string? Endpoint);

public sealed record ForRestMcpBrowserElementView(
    bool Found,
    string Tag,
    string Id,
    string Text,
    string Css,
    string Xpath,
    string Role,
    string Name,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record ForRestMcpBrowserSnapshotView(
    bool Available,
    string Url,
    string Title,
    IReadOnlyList<ForRestMcpBrowserElementView> Elements);

#endregion

/// <summary>
/// Bridge supplied by the desktop app so the MCP tools can reach the live
/// workbench: the persisted workspace tree, the active document, request
/// execution, run history, and server settings. When no host is registered
/// the MCP server still runs and serves the passive tools (docs search,
/// language reference, provider list, payload catalog) but the canvas-aware
/// tools return a "not wired" error string.
///
/// The implementation operates on the same persisted workbench state and the
/// same execution-history store the desktop UI uses, so MCP edits and runs are
/// durable and surface in the app on reload. Read operations that return script
/// source are expected to have secret values redacted by the caller; write
/// operations restore the original secret declarations the host already holds.
/// </summary>
public interface IForRestMcpHost
{
    #region Active document

    Task<ForRestMcpDocumentSnapshot?> GetActiveDocument(CancellationToken cancellationToken);

    Task<ForRestMcpUpdateResult> ReplaceActiveDocument(string newSource, CancellationToken cancellationToken);

    Task<ForRestMcpMutationResult> SetActiveDocument(string workspaceId, string location, CancellationToken cancellationToken);

    #endregion

    #region Workspaces

    Task<IReadOnlyList<ForRestMcpWorkspaceSummary>> ListWorkspaces(CancellationToken cancellationToken);

    Task<ForRestMcpWorkspaceSummary?> GetWorkspace(string workspaceId, CancellationToken cancellationToken);

    Task<ForRestMcpMutationResult> CreateWorkspace(string name, CancellationToken cancellationToken);

    Task<ForRestMcpMutationResult> RenameWorkspace(string workspaceId, string newName, CancellationToken cancellationToken);

    Task<ForRestMcpMutationResult> DeleteWorkspace(string workspaceId, CancellationToken cancellationToken);

    #endregion

    #region Scripts

    Task<ForRestMcpScriptDetail?> GetScript(string workspaceId, string location, CancellationToken cancellationToken);

    Task<ForRestMcpMutationResult> CreateScript(string workspaceId, string name, string source, CancellationToken cancellationToken);

    Task<ForRestMcpMutationResult> UpdateScript(string workspaceId, string location, string newSource, CancellationToken cancellationToken);

    Task<ForRestMcpMutationResult> RenameScript(string workspaceId, string location, string newName, CancellationToken cancellationToken);

    Task<ForRestMcpMutationResult> DeleteScript(string workspaceId, string location, CancellationToken cancellationToken);

    #endregion

    #region Compile / execute

    ForRestMcpCompileResult CompileScript(string workspaceId, string source);

    Task<ForRestMcpExecutionResult> ExecuteScript(
        string workspaceId,
        string source,
        string? requestName,
        CancellationToken cancellationToken);

    Task<ForRestMcpExecutionResult> ExecuteStoredScript(
        string workspaceId,
        string location,
        CancellationToken cancellationToken);

    #endregion

    #region Results / history

    Task<IReadOnlyList<ForRestMcpRunSummary>> ListRuns(string workspaceId, int max, CancellationToken cancellationToken);

    Task<ForRestMcpRunDetail?> GetRun(string workspaceId, string runId, CancellationToken cancellationToken);

    #endregion

    #region Settings

    ForRestMcpServerSettingsView GetServerSettings();

    string GetSettingsText();

    ForRestMcpMutationResult UpdateSettingsText(string rawToml);

    #endregion

    #region Browser automation

    /// <summary>Whether a live browser pane is connected and ready to be driven.</summary>
    bool BrowserAvailable { get; }

    Task<string> BrowserNavigate(string url, CancellationToken cancellationToken);

    Task<ForRestMcpBrowserSnapshotView> BrowserSnapshot(CancellationToken cancellationToken);

    /// <summary>Returns a base64-encoded PNG screenshot of the current page.</summary>
    Task<string> BrowserScreenshot(CancellationToken cancellationToken);

    Task<ForRestMcpBrowserElementView> BrowserQuery(string target, CancellationToken cancellationToken);

    Task<string> BrowserClick(string target, CancellationToken cancellationToken);

    Task<string> BrowserType(string target, string text, CancellationToken cancellationToken);

    Task<string> BrowserPress(string keys, CancellationToken cancellationToken);

    Task<ForRestMcpBrowserElementView> BrowserWaitFor(string target, int timeoutMs, CancellationToken cancellationToken);

    Task<string> BrowserEvaluate(string expression, CancellationToken cancellationToken);

    #endregion

    #region App surface

    /// <summary>
    /// Brings something into view in the running desktop app (changes the selected tab, selects a
    /// document/run, or focuses a pane) so the user looks at it. Requires the app UI to be running.
    /// </summary>
    Task<ForRestMcpMutationResult> ShowInApp(string target, string? id, CancellationToken cancellationToken);

    #endregion
}
