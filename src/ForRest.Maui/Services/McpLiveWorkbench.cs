using System.Collections.Generic;
using System.Threading.Tasks;

namespace ForRest.Maui.Services;

#region Neutral DTOs

/// <summary>Outcome of a live workbench mutation. <see cref="Id"/> carries a new
/// workspace id or script location when one is produced.</summary>
public sealed record McpWorkbenchResult(bool Succeeded, string? Message, string? Id = null)
{
    public static McpWorkbenchResult Ok(string? message = null, string? id = null) => new(true, message, id);

    public static McpWorkbenchResult Fail(string message) => new(false, message);
}

public sealed record McpWorkbenchScript(string Name, string Method, string UrlTemplate, string Location);

public sealed record McpWorkbenchWorkspace(string Id, string Name, IReadOnlyList<McpWorkbenchScript> Scripts);

public sealed record McpWorkbenchDocument(string Location, string Title, string Source, string WorkspaceName);

public sealed record McpWorkbenchScriptDetail(
    string WorkspaceId,
    string WorkspaceName,
    string Location,
    string Name,
    string Method,
    string Summary,
    string Source);

#endregion

/// <summary>
/// Live view of the running workbench, implemented by the page view model so the
/// MCP server can create/edit/select workspaces and scripts and have the changes
/// appear on screen immediately and persist through the same pipeline the UI
/// uses (rather than writing the state file underneath the running app, which the
/// view model would overwrite). All operations marshal onto the UI thread.
///
/// Returns neutral DTOs (no ForRest.Mcp dependency) because the view model is
/// compiled for mobile targets that do not reference the MCP assembly. Secret
/// redaction/restoration is handled by the MCP host before/after calling here.
/// </summary>
public interface IMcpWorkbenchBridge
{
    Task<IReadOnlyList<McpWorkbenchWorkspace>> ListWorkspaces();

    Task<McpWorkbenchWorkspace?> GetWorkspace(string workspaceId);

    Task<McpWorkbenchDocument?> GetActiveDocument();

    Task<McpWorkbenchScriptDetail?> GetScript(string workspaceId, string location);

    Task<McpWorkbenchResult> CreateWorkspace(string name);

    Task<McpWorkbenchResult> RenameWorkspace(string workspaceId, string newName);

    Task<McpWorkbenchResult> DeleteWorkspace(string workspaceId);

    Task<McpWorkbenchResult> CreateScript(string workspaceId, string name, string source);

    Task<McpWorkbenchResult> UpdateScript(string workspaceId, string location, string newSource);

    Task<McpWorkbenchResult> RenameScript(string workspaceId, string location, string newName);

    Task<McpWorkbenchResult> DeleteScript(string workspaceId, string location);

    Task<McpWorkbenchResult> SetActiveDocument(string workspaceId, string location);

    Task<McpWorkbenchResult> ReplaceActiveDocument(string newSource);

    /// <summary>Brings a tab, document, run, or workspace into view so the user looks at it.</summary>
    Task<McpWorkbenchResult> ShowInApp(string target, string? id);
}

/// <summary>
/// Mutable singleton holder the running page view model registers itself into so
/// the (lazily resolved) MCP host can reach the live workbench when the desktop
/// app is up, and fall back to the persisted store when it is not.
/// </summary>
public sealed class McpLiveWorkbenchAccessor
{
    public IMcpWorkbenchBridge? Current { get; set; }
}
