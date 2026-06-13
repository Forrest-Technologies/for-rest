using System.Text.Encodings.Web;
using System.Text.Json;

namespace ForRest.Services.AI;

#region Host Contracts

/// <summary>
/// A single request script as seen by the workspace assistant. Mirrors the shape of
/// <see cref="AiActiveDocumentSnapshot"/> but is scoped to an arbitrary script in the
/// workspace tree rather than the one document open on the editor canvas.
/// </summary>
public sealed record AiWorkspaceScriptDocument(
    string ScriptId,
    string Name,
    string Language,
    string SourceText,
    IReadOnlyList<AiActiveDocumentDiagnostic> Diagnostics);

/// <summary>
/// Host the workspace assistant operates against. Where <see cref="IAiActiveDocumentHost"/>
/// is bound to the single document open on the editor canvas, this host can enumerate, read,
/// create, and rewrite any request script in the active workspace — so a single prompt can
/// scaffold or refactor a whole suite of requests instead of just the file in focus.
/// </summary>
public interface IAiWorkspaceHost
{
    AiWorkspaceContext? GetWorkspaceContext();

    AiWorkspaceScriptDocument? ReadScript(string scriptId);

    AiActiveDocumentUpdateResult CreateScript(string name, string sourceText);

    AiActiveDocumentUpdateResult UpdateScript(string scriptId, string sourceText);
}

#endregion

#region Tool Catalog

public interface IAiWorkspaceToolCatalog
{
    IReadOnlyList<AiToolDescriptor> GetTools(AiSettings settings, IAiWorkspaceHost? workspaceHost);
}

public sealed class AiWorkspaceToolCatalog : IAiWorkspaceToolCatalog
{
    public IReadOnlyList<AiToolDescriptor> GetTools(AiSettings settings, IAiWorkspaceHost? workspaceHost)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled ||
            workspaceHost is null ||
            !settings.Tools.EnableDocumentPatch)
        {
            return [];
        }

        return
        [
            new(
                "list_workspace_scripts",
                "List every request script in the current workspace with its id, name, method, and URL template.",
                "Call this first so you know which scripts already exist before creating duplicates or deciding what to edit. Use the returned script ids with read_workspace_script and update_workspace_script.",
                MutatesDocument: false),
            new(
                "read_workspace_script",
                "Read the full source and compiler diagnostics for a single workspace script by id.",
                "Call this before update_workspace_script so the rewrite is grounded in the current source and any diagnostics. The returned source excludes inline chat scaffolding and redacts secret values.",
                MutatesDocument: false),
            new(
                "create_workspace_script",
                "Create a new request script in the current workspace from a name and full ForRest source text.",
                "Use when the user asks to add one or more new requests. Provide a concise name and complete runnable ForRest source. The returned scriptId can be used immediately with read_workspace_script or update_workspace_script.",
                MutatesDocument: true),
            new(
                "update_workspace_script",
                "Replace the entire source of an existing workspace script identified by id.",
                "Use to rewrite a request you previously read. Provide the scriptId from list_workspace_scripts and the complete replacement source. Secret declarations are preserved automatically even if you only saw redacted values.",
                MutatesDocument: true),
        ];
    }
}

#endregion

#region Tool Service

public interface IAiWorkspaceToolService
{
    string ListScripts(AiSettings settings, IAiWorkspaceHost? workspaceHost);

    string ReadScript(AiSettings settings, IAiWorkspaceHost? workspaceHost, string scriptId);

    string CreateScript(AiSettings settings, IAiWorkspaceHost? workspaceHost, string name, string sourceText);

    string UpdateScript(AiSettings settings, IAiWorkspaceHost? workspaceHost, string scriptId, string updatedSourceText);
}

public sealed class AiWorkspaceToolService : IAiWorkspaceToolService
{
    #region Private Fields

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Keep quotes unescaped so ForRest source round-trips legibly through the
        // tool-result JSON envelope — matching AiActiveDocumentToolService so reasoning
        // models never see double-escaped `"` tokens in script bodies.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    #endregion

    #region Public Methods

    public string ListScripts(AiSettings settings, IAiWorkspaceHost? workspaceHost)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (TryGuard(settings, workspaceHost, out string? guard))
        {
            return guard;
        }

        AiWorkspaceContext? context = workspaceHost!.GetWorkspaceContext();
        if (context is null)
        {
            return SerializeFailure("No workspace context is available.");
        }

        return Serialize(new
        {
            succeeded = true,
            workspace = new { id = context.WorkspaceId, name = context.WorkspaceName },
            scripts = context.Scripts.Select(static script => new
            {
                id = script.ScriptId,
                name = script.Name,
                method = script.Method,
                url = script.UrlTemplate,
            }),
            errors = Array.Empty<string>(),
        });
    }

    public string ReadScript(AiSettings settings, IAiWorkspaceHost? workspaceHost, string scriptId)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (TryGuard(settings, workspaceHost, out string? guard))
        {
            return guard;
        }

        if (string.IsNullOrWhiteSpace(scriptId))
        {
            return SerializeFailure("A scriptId is required. Call list_workspace_scripts to discover ids.");
        }

        AiWorkspaceScriptDocument? document = workspaceHost!.ReadScript(scriptId);
        if (document is null)
        {
            return SerializeFailure($"No workspace script was found with id '{scriptId}'.");
        }

        AiWorkspaceScriptDocument redacted = document with
        {
            SourceText = AiActiveDocumentToolService.RedactSecrets(document.SourceText),
        };

        return Serialize(new
        {
            succeeded = true,
            document = redacted,
            errors = Array.Empty<string>(),
        });
    }

    public string CreateScript(AiSettings settings, IAiWorkspaceHost? workspaceHost, string name, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (TryGuard(settings, workspaceHost, out string? guard))
        {
            return guard;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return SerializeFailure("A script name is required.");
        }

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return SerializeFailure("Script source text is required.");
        }

        if (sourceText.Length > settings.Tools.MaxPatchCharacters)
        {
            return SerializeFailure($"The script exceeds the configured maximum of {settings.Tools.MaxPatchCharacters} characters.");
        }

        AiActiveDocumentUpdateResult result = workspaceHost!.CreateScript(name, sourceText);
        if (!result.Succeeded)
        {
            return Serialize(new
            {
                succeeded = false,
                errors = new[] { result.Message },
                diagnostics = result.Diagnostics,
                retryWithReplace = result.RetryWithReplace,
            });
        }

        return Serialize(new
        {
            succeeded = true,
            scriptName = name,
            // The host echoes the new script id back through UpdatedText so the agent can
            // immediately read or update the request it just created.
            scriptId = result.UpdatedText,
            errors = Array.Empty<string>(),
        });
    }

    public string UpdateScript(AiSettings settings, IAiWorkspaceHost? workspaceHost, string scriptId, string updatedSourceText)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (TryGuard(settings, workspaceHost, out string? guard))
        {
            return guard;
        }

        if (string.IsNullOrWhiteSpace(scriptId))
        {
            return SerializeFailure("A scriptId is required. Call list_workspace_scripts to discover ids.");
        }

        if ((updatedSourceText ?? string.Empty).Length > settings.Tools.MaxPatchCharacters)
        {
            return SerializeFailure($"The replacement exceeds the configured maximum of {settings.Tools.MaxPatchCharacters} characters.");
        }

        AiWorkspaceScriptDocument? existing = workspaceHost!.ReadScript(scriptId);
        if (existing is null)
        {
            return SerializeFailure($"No workspace script was found with id '{scriptId}'.");
        }

        // The agent only ever sees redacted secret values, so splice the original secret
        // declarations back in before the host commits the rewrite — credentials survive
        // workspace-level rewrites exactly as they do for the active-document tools.
        string restoredSourceText = AiActiveDocumentToolService.RestoreOriginalSecrets(
            existing.SourceText,
            updatedSourceText ?? string.Empty);

        AiActiveDocumentUpdateResult result = workspaceHost.UpdateScript(scriptId, restoredSourceText);
        if (!result.Succeeded)
        {
            return Serialize(new
            {
                succeeded = false,
                scriptId,
                patchedText = string.IsNullOrWhiteSpace(result.UpdatedText)
                    ? AiActiveDocumentToolService.RedactSecrets(restoredSourceText)
                    : AiActiveDocumentToolService.RedactSecrets(result.UpdatedText),
                retryWithReplace = result.RetryWithReplace,
                errors = new[] { result.Message },
                diagnostics = result.Diagnostics,
            });
        }

        return Serialize(new
        {
            succeeded = true,
            scriptId,
            patchedText = AiActiveDocumentToolService.RedactSecrets(restoredSourceText),
            errors = Array.Empty<string>(),
        });
    }

    #endregion

    #region Private Methods

    private static bool TryGuard(AiSettings settings, IAiWorkspaceHost? workspaceHost, out string failure)
    {
        if (!settings.Enabled)
        {
            failure = SerializeFailure("AI is disabled.");
            return true;
        }

        if (workspaceHost is null)
        {
            failure = SerializeFailure("No workspace host is available.");
            return true;
        }

        failure = string.Empty;
        return false;
    }

    private static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string SerializeFailure(params string[] errors)
    {
        return Serialize(new
        {
            succeeded = false,
            errors,
        });
    }

    #endregion
}

#endregion

#region Conversation Service

public sealed record AiWorkspaceConversationRequest(
    string WorkspaceId,
    string WorkspaceName,
    string Language,
    string SourceText,
    int CursorLineNumber,
    AiSettings Settings,
    IAiWorkspaceHost WorkspaceHost);

public sealed record AiWorkspaceConversationResult(
    bool Handled,
    bool Succeeded,
    string UpdatedText,
    string StatusText,
    string DebugText,
    string ResponseText = "",
    int? PromptLineNumber = null,
    AiInlineConversationUpdateKind UpdateKind = AiInlineConversationUpdateKind.ResponseOnly)
{
    public static AiWorkspaceConversationResult NotHandled(string sourceText)
    {
        return new(false, true, sourceText ?? string.Empty, string.Empty, string.Empty);
    }
}

public interface IAiWorkspaceConversationService
{
    Task<AiWorkspaceConversationResult> TryHandleAsync(AiWorkspaceConversationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Drives the pinned "Workspace Assistant" conversation. It reuses the same inline
/// <c>##</c>/<c>#&gt;</c> transcript syntax as the file-level assistant, but routes the turn
/// through <see cref="IAiWorkspaceHost"/> so the agent's tools operate across the whole
/// workspace instead of a single open request document.
/// </summary>
public sealed class AiWorkspaceConversationService(IAiTurnExecutor turnExecutor) : IAiWorkspaceConversationService
{
    #region Private Fields

    private readonly IAiTurnExecutor _turnExecutor = turnExecutor;

    #endregion

    #region Public Methods

    public async Task<AiWorkspaceConversationResult> TryHandleAsync(
        AiWorkspaceConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.WorkspaceHost);

        AiInlineConversationPrompt? prompt = AiInlineConversationPromptResolver.ResolveActionablePrompt(
            request.SourceText,
            request.CursorLineNumber);
        if (prompt is null)
        {
            return AiWorkspaceConversationResult.NotHandled(request.SourceText);
        }

        AiTurnExecutionResult turn = await _turnExecutor.ExecuteAsync(
            new AiTurnExecutionRequest(
                ConversationId: BuildConversationId(request.WorkspaceId),
                Objective: BuildObjective(request),
                Prompt: prompt.PromptText,
                Settings: request.Settings,
                ActiveDocumentHost: null,
                WorkspaceHost: request.WorkspaceHost),
            cancellationToken);

        string updatedText = RenderResponse(request.SourceText, prompt.LineNumber, turn.ResponseText);

        return new(
            Handled: true,
            Succeeded: turn.Succeeded,
            UpdatedText: updatedText,
            StatusText: BuildStatusText(turn),
            DebugText: turn.DebugTrace,
            ResponseText: turn.ResponseText,
            PromptLineNumber: prompt.LineNumber,
            UpdateKind: AiInlineConversationUpdateKind.ResponseOnly);
    }

    #endregion

    #region Private Methods

    private static string BuildConversationId(string workspaceId)
    {
        return string.IsNullOrWhiteSpace(workspaceId)
            ? "workspace-assistant"
            : $"workspace-assistant:{workspaceId.Trim()}";
    }

    private static string BuildObjective(AiWorkspaceConversationRequest request)
    {
        string workspaceName = string.IsNullOrWhiteSpace(request.WorkspaceName)
            ? "this workspace"
            : request.WorkspaceName.Trim();
        return
            $"You are the For-Rest workspace assistant for the workspace '{workspaceName}'. " +
            "Operate across the entire workspace, not a single request document. " +
            "Call list_workspace_scripts to see what already exists, and read_workspace_script before changing a script so edits are grounded in its current source and diagnostics. " +
            "Use create_workspace_script to add new request scripts and update_workspace_script to rewrite existing ones by id. " +
            "When the user asks to scaffold, test, or refactor an API surface, prefer documented request mutation, stash, loop, and top-level expect patterns, and leave every script as runnable ForRest source. " +
            "Choose reasonable defaults instead of asking clarification questions when the request is actionable, and reply briefly once the workspace has been updated.";
    }

    private static string BuildStatusText(AiTurnExecutionResult turn)
    {
        if (!turn.Succeeded)
        {
            return "Workspace assistant could not complete the request.";
        }

        return "Workspace assistant updated the workspace.";
    }

    private static string RenderResponse(string sourceText, int promptLineNumber, string responseText)
    {
        string withResponse = AiInlineConversationFormatter.ApplyResponse(sourceText, promptLineNumber, responseText);
        return AiInlineConversationFormatter.EnsureFreshPromptAfterConversation(withResponse, promptLineNumber);
    }

    #endregion
}

#endregion
