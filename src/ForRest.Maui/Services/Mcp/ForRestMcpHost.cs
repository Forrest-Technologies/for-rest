#if WINDOWS || MACCATALYST
using System.Text;
using ForRest.Browser;
using ForRest.Maui.Theming;
using ForRest.Mcp;
using ForRest.Models;
using ForRest.Repositories;
using ForRest.Scripting;
using ForRest.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ForRest.Maui.Services.Mcp;

/// <summary>
/// Desktop implementation of <see cref="IForRestMcpHost"/>. Bridges the MCP
/// tools to the same persisted workbench (<see cref="RequestWorkbenchStateStore"/>),
/// execution pipeline (<see cref="IForRestScriptExecutionService"/>), execution
/// history (<see cref="IExecutionHistoryRepository"/>), and settings store the
/// desktop UI uses, so MCP edits and runs are durable and visible in the app on
/// reload. The host deliberately does not depend on the page view model; it
/// operates on the shared persisted state, which keeps it testable and avoids
/// coupling external automation to UI lifetime.
///
/// Writes are serialized by a mutation gate so concurrent MCP calls cannot lose
/// each other's edits. The running UI keeps its own in-memory copy of the
/// workbench, so MCP edits surface there on the next load/refresh rather than
/// instantly.
/// </summary>
public sealed class ForRestMcpHost(
    RequestWorkbenchStateStore stateStore,
    IForRestScriptExecutionService scriptExecutionService,
    IExecutionHistoryRepository executionHistoryRepository,
    IWorkbenchMcpSettingsProvider settingsProvider,
    ThemeConfigStore themeConfigStore,
    ThemeConfigParser themeConfigParser,
    McpLiveWorkbenchAccessor liveWorkbenchAccessor,
    IBrowserAutomationProvider browserProvider,
    ILogger<ForRestMcpHost>? logger = null) : IForRestMcpHost
{
    #region Private Fields

    private static readonly IReadOnlyList<RequestWorkbenchWorkspaceState> NoDefaults = [];

    private readonly ILogger<ForRestMcpHost> logger = logger ?? NullLogger<ForRestMcpHost>.Instance;
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    /// <summary>The live workbench when the desktop UI is running; null otherwise.</summary>
    private IMcpWorkbenchBridge? Live => liveWorkbenchAccessor.Current;

    #endregion

    #region Active document

    public async Task<ForRestMcpDocumentSnapshot?> GetActiveDocument(CancellationToken cancellationToken)
    {
        if (Live is { } live)
        {
            McpWorkbenchDocument? document = await live.GetActiveDocument();
            return document is null
                ? null
                : new ForRestMcpDocumentSnapshot(document.Location, document.Title, "forrest", document.Source, document.WorkspaceName);
        }

        RequestWorkbenchState state = await LoadState(cancellationToken);
        RequestWorkbenchWorkspaceState? workspace = SelectedWorkspace(state);
        RequestWorkbenchDocumentState? document2 = SelectedDocument(workspace);
        if (workspace is null || document2 is null)
        {
            return null;
        }

        return new ForRestMcpDocumentSnapshot(
            DocumentId: document2.Location,
            Title: document2.Title,
            Language: "forrest",
            SourceText: document2.RequestSource,
            WorkspaceName: workspace.Name);
    }

    public async Task<ForRestMcpUpdateResult> ReplaceActiveDocument(string newSource, CancellationToken cancellationToken)
    {
        if (Live is { } live)
        {
            McpWorkbenchDocument? active = await live.GetActiveDocument();
            string liveRestored = McpSecretRedactor.Restore(active?.Source, newSource);
            McpWorkbenchResult liveResult = await live.ReplaceActiveDocument(liveRestored);
            return liveResult.Succeeded
                ? ForRestMcpUpdateResult.Success()
                : ForRestMcpUpdateResult.Failure(liveResult.Message ?? "Replace failed.");
        }

        return await Mutate(cancellationToken, state =>
        {
            RequestWorkbenchWorkspaceState? workspace = SelectedWorkspace(state);
            RequestWorkbenchDocumentState? document = SelectedDocument(workspace);
            if (workspace is null || document is null)
            {
                return (false, ForRestMcpUpdateResult.Failure("No active document is open."), state);
            }

            string restored = McpSecretRedactor.Restore(document.RequestSource, newSource);
            ReplaceDocument(workspace, document, UpdateSource(workspace.Id, document, restored));
            return (true, ForRestMcpUpdateResult.Success(), state);
        });
    }

    public async Task<ForRestMcpMutationResult> SetActiveDocument(string workspaceId, string location, CancellationToken cancellationToken)
    {
        if (Live is { } live)
        {
            return MapLive(await live.SetActiveDocument(workspaceId, location));
        }

        return await Mutate(cancellationToken, state =>
        {
            RequestWorkbenchWorkspaceState? workspace = FindWorkspace(state, workspaceId);
            if (workspace is null)
            {
                return (false, ForRestMcpMutationResult.Fail($"No workspace found with id '{workspaceId}'."), state);
            }

            RequestWorkbenchDocumentState? document = FindDocument(workspace, location);
            if (document is null)
            {
                return (false, ForRestMcpMutationResult.Fail($"No script found at '{location}' in workspace '{workspaceId}'."), state);
            }

            int index = state.Workspaces.FindIndex(item => item.Id == workspace.Id);
            state.Workspaces[index] = workspace with { SelectedDocumentLocation = document.Location };
            state = state with { SelectedWorkspaceId = workspace.Id };
            return (true, ForRestMcpMutationResult.Ok("Active document selected.", document.Location), state);
        });
    }

    #endregion

    #region Workspaces

    public async Task<IReadOnlyList<ForRestMcpWorkspaceSummary>> ListWorkspaces(CancellationToken cancellationToken)
    {
        if (Live is { } live)
        {
            return [.. (await live.ListWorkspaces()).Select(MapLive)];
        }

        RequestWorkbenchState state = await LoadState(cancellationToken);
        return [.. state.Workspaces.Select(MapWorkspace)];
    }

    public async Task<ForRestMcpWorkspaceSummary?> GetWorkspace(string workspaceId, CancellationToken cancellationToken)
    {
        if (Live is { } live)
        {
            McpWorkbenchWorkspace? workspace = await live.GetWorkspace(workspaceId);
            return workspace is null ? null : MapLive(workspace);
        }

        RequestWorkbenchState state = await LoadState(cancellationToken);
        RequestWorkbenchWorkspaceState? found = FindWorkspace(state, workspaceId);
        return found is null ? null : MapWorkspace(found);
    }

    public async Task<ForRestMcpMutationResult> CreateWorkspace(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ForRestMcpMutationResult.Fail("Workspace name is required.");
        }

        if (Live is { } live)
        {
            return MapLive(await live.CreateWorkspace(name));
        }

        return await Mutate(cancellationToken, state =>
        {
            RequestWorkbenchWorkspaceState workspace = new()
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
            };
            state.Workspaces.Add(workspace);
            return (true, ForRestMcpMutationResult.Ok($"Workspace '{workspace.Name}' created.", workspace.Id.ToString()), state);
        });
    }

    public async Task<ForRestMcpMutationResult> RenameWorkspace(string workspaceId, string newName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return ForRestMcpMutationResult.Fail("New workspace name is required.");
        }

        if (Live is { } live)
        {
            return MapLive(await live.RenameWorkspace(workspaceId, newName));
        }

        return await Mutate(cancellationToken, state =>
        {
            RequestWorkbenchWorkspaceState? workspace = FindWorkspace(state, workspaceId);
            if (workspace is null)
            {
                return (false, ForRestMcpMutationResult.Fail($"No workspace found with id '{workspaceId}'."), state);
            }

            int index = state.Workspaces.FindIndex(item => item.Id == workspace.Id);
            state.Workspaces[index] = workspace with { Name = newName.Trim() };
            return (true, ForRestMcpMutationResult.Ok("Workspace renamed.", workspace.Id.ToString()), state);
        });
    }

    public async Task<ForRestMcpMutationResult> DeleteWorkspace(string workspaceId, CancellationToken cancellationToken)
    {
        if (Live is { } live)
        {
            return MapLive(await live.DeleteWorkspace(workspaceId));
        }

        return await Mutate(cancellationToken, state =>
        {
            RequestWorkbenchWorkspaceState? workspace = FindWorkspace(state, workspaceId);
            if (workspace is null)
            {
                return (false, ForRestMcpMutationResult.Fail($"No workspace found with id '{workspaceId}'."), state);
            }

            state.Workspaces.RemoveAll(item => item.Id == workspace.Id);
            if (state.SelectedWorkspaceId == workspace.Id)
            {
                state = state with { SelectedWorkspaceId = state.Workspaces.FirstOrDefault()?.Id ?? Guid.Empty };
            }

            return (true, ForRestMcpMutationResult.Ok("Workspace deleted."), state);
        });
    }

    #endregion

    #region Scripts

    public async Task<ForRestMcpScriptDetail?> GetScript(string workspaceId, string location, CancellationToken cancellationToken)
    {
        if (Live is { } live)
        {
            McpWorkbenchScriptDetail? detail = await live.GetScript(workspaceId, location);
            return detail is null
                ? null
                : new ForRestMcpScriptDetail(detail.WorkspaceId, detail.WorkspaceName, detail.Location, detail.Name, detail.Method, detail.Summary, detail.Source, string.Empty);
        }

        RequestWorkbenchState state = await LoadState(cancellationToken);
        RequestWorkbenchWorkspaceState? workspace = FindWorkspace(state, workspaceId);
        RequestWorkbenchDocumentState? document = FindDocument(workspace, location);
        if (workspace is null || document is null)
        {
            return null;
        }

        return new ForRestMcpScriptDetail(
            WorkspaceId: workspace.Id.ToString(),
            WorkspaceName: workspace.Name,
            Location: document.Location,
            Name: document.Title,
            Method: document.Method,
            Summary: document.Summary,
            Source: document.RequestSource,
            PreRequestScript: document.PreRequestScript);
    }

    public async Task<ForRestMcpMutationResult> CreateScript(string workspaceId, string name, string source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ForRestMcpMutationResult.Fail("Script name is required.");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return ForRestMcpMutationResult.Fail("Script source is required.");
        }

        if (Live is { } live)
        {
            return MapLive(await live.CreateScript(workspaceId, name, source));
        }

        return await Mutate(cancellationToken, state =>
        {
            RequestWorkbenchWorkspaceState? workspace = FindWorkspace(state, workspaceId);
            if (workspace is null)
            {
                return (false, ForRestMcpMutationResult.Fail($"No workspace found with id '{workspaceId}'."), state);
            }

            string location = BuildLocation(workspace.Documents.Select(static document => document.Location), name);
            (string method, string summary) = DescribeSource(workspace.Id, source, name);
            workspace.Documents.Add(new RequestWorkbenchDocumentState
            {
                Title = name.Trim(),
                Method = method,
                Summary = summary,
                Location = location,
                RequestSource = source,
            });

            return (true, ForRestMcpMutationResult.Ok($"Script '{name.Trim()}' created.", location), state);
        });
    }

    public async Task<ForRestMcpMutationResult> UpdateScript(string workspaceId, string location, string newSource, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newSource))
        {
            return ForRestMcpMutationResult.Fail("Replacement source is required.");
        }

        if (Live is { } live)
        {
            McpWorkbenchScriptDetail? detail = await live.GetScript(workspaceId, location);
            string liveRestored = McpSecretRedactor.Restore(detail?.Source, newSource);
            return MapLive(await live.UpdateScript(workspaceId, location, liveRestored));
        }

        return await Mutate(cancellationToken, state =>
        {
            RequestWorkbenchWorkspaceState? workspace = FindWorkspace(state, workspaceId);
            RequestWorkbenchDocumentState? document = FindDocument(workspace, location);
            if (workspace is null || document is null)
            {
                return (false, ForRestMcpMutationResult.Fail($"No script found at '{location}' in workspace '{workspaceId}'."), state);
            }

            string restored = McpSecretRedactor.Restore(document.RequestSource, newSource);
            ReplaceDocument(workspace, document, UpdateSource(workspace.Id, document, restored));
            return (true, ForRestMcpMutationResult.Ok("Script updated.", document.Location), state);
        });
    }

    public async Task<ForRestMcpMutationResult> RenameScript(string workspaceId, string location, string newName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return ForRestMcpMutationResult.Fail("New script name is required.");
        }

        if (Live is { } live)
        {
            return MapLive(await live.RenameScript(workspaceId, location, newName));
        }

        return await Mutate(cancellationToken, state =>
        {
            RequestWorkbenchWorkspaceState? workspace = FindWorkspace(state, workspaceId);
            RequestWorkbenchDocumentState? document = FindDocument(workspace, location);
            if (workspace is null || document is null)
            {
                return (false, ForRestMcpMutationResult.Fail($"No script found at '{location}' in workspace '{workspaceId}'."), state);
            }

            ReplaceDocument(workspace, document, document with { Title = newName.Trim() });
            return (true, ForRestMcpMutationResult.Ok("Script renamed.", document.Location), state);
        });
    }

    public async Task<ForRestMcpMutationResult> DeleteScript(string workspaceId, string location, CancellationToken cancellationToken)
    {
        if (Live is { } live)
        {
            return MapLive(await live.DeleteScript(workspaceId, location));
        }

        return await Mutate(cancellationToken, state =>
        {
            RequestWorkbenchWorkspaceState? workspace = FindWorkspace(state, workspaceId);
            RequestWorkbenchDocumentState? document = FindDocument(workspace, location);
            if (workspace is null || document is null)
            {
                return (false, ForRestMcpMutationResult.Fail($"No script found at '{location}' in workspace '{workspaceId}'."), state);
            }

            workspace.Documents.RemoveAll(item => string.Equals(item.Location, document.Location, StringComparison.OrdinalIgnoreCase));
            return (true, ForRestMcpMutationResult.Ok("Script deleted."), state);
        });
    }

    #endregion

    #region Compile / execute

    public ForRestMcpCompileResult CompileScript(string workspaceId, string source)
    {
        Guid id = TryParseWorkspaceId(workspaceId);
        ForRestScriptCompilationResult compilation = scriptExecutionService.Compile(source, id);
        return MapCompilation(compilation);
    }

    public async Task<ForRestMcpExecutionResult> ExecuteScript(
        string workspaceId,
        string source,
        string? requestName,
        CancellationToken cancellationToken)
    {
        Guid id = TryParseWorkspaceId(workspaceId);
        string name = string.IsNullOrWhiteSpace(requestName) ? "MCP Request" : requestName!;
        return await ExecuteSource(id, name, source, cancellationToken);
    }

    public async Task<ForRestMcpExecutionResult> ExecuteStoredScript(
        string workspaceId,
        string location,
        CancellationToken cancellationToken)
    {
        if (Live is { } live)
        {
            McpWorkbenchScriptDetail? detail = await live.GetScript(workspaceId, location);
            if (detail is null)
            {
                return Failed($"No script found at '{location}' in workspace '{workspaceId}'.");
            }

            Guid liveId = TryParseWorkspaceId(detail.WorkspaceId);
            return await ExecuteSource(liveId, detail.Name, detail.Source, cancellationToken);
        }

        RequestWorkbenchState state = await LoadState(cancellationToken);
        RequestWorkbenchWorkspaceState? workspace = FindWorkspace(state, workspaceId);
        RequestWorkbenchDocumentState? document = FindDocument(workspace, location);
        if (workspace is null || document is null)
        {
            return Failed($"No script found at '{location}' in workspace '{workspaceId}'.");
        }

        return await ExecuteSource(workspace.Id, document.Title, document.RequestSource, cancellationToken);
    }

    private async Task<ForRestMcpExecutionResult> ExecuteSource(Guid workspaceId, string name, string source, CancellationToken cancellationToken)
    {
        WorkspaceSnapshot snapshot = new()
        {
            Workspace = new WorkspaceDefinition { Id = workspaceId, Name = name },
        };

        ForRestScriptExecutionOutcome outcome;
        try
        {
            outcome = await scriptExecutionService.Execute(
                new AppProfile(),
                snapshot,
                source,
                environment: null,
                defaultRequestName: name,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "MCP execution of '{RequestName}' failed.", name);
            return Failed(exception.Message);
        }

        ForRestMcpCompileResult compile = MapCompilation(outcome.Compilation);
        if (outcome.Execution is null)
        {
            return new ForRestMcpExecutionResult(
                Succeeded: false,
                State: "CompileFailed",
                Diagnostics: compile.Diagnostics,
                RunId: null,
                Response: null,
                Tests: [],
                Logs: [],
                Stash: null,
                ErrorMessage: "Compilation failed; no request was sent.");
        }

        RequestExecutionResult execution = outcome.Execution;
        ExecutionRun? primaryRun = execution.Runs.LastOrDefault();
        bool succeeded = compile.Succeeded && execution.State == ExecutionState.Completed;

        return new ForRestMcpExecutionResult(
            Succeeded: succeeded,
            State: execution.State.ToString(),
            Diagnostics: compile.Diagnostics,
            RunId: primaryRun?.Id.ToString(),
            Response: MapResponse(execution.LatestResponse),
            Tests: [.. execution.Tests.Select(MapTest)],
            Logs: [.. execution.ConsoleEntries.Select(MapLog)],
            Stash: MapStash(execution.Stash),
            ErrorMessage: primaryRun?.ErrorMessage,
            SecretValues: CollectSecretValues(execution.RuntimeVariables));
    }

    #endregion

    #region Results / history

    public async Task<IReadOnlyList<ForRestMcpRunSummary>> ListRuns(string workspaceId, int max, CancellationToken cancellationToken)
    {
        Guid id = TryParseWorkspaceId(workspaceId);
        List<ExecutionRun> runs = await executionHistoryRepository.Load(id, cancellationToken);
        return [.. runs.Take(max).Select(MapRunSummary)];
    }

    public async Task<ForRestMcpRunDetail?> GetRun(string workspaceId, string runId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(runId, out Guid parsedRunId))
        {
            return null;
        }

        Guid id = TryParseWorkspaceId(workspaceId);
        List<ExecutionRun> runs = await executionHistoryRepository.Load(id, cancellationToken);
        ExecutionRun? run = runs.FirstOrDefault(item => item.Id == parsedRunId);
        if (run is null)
        {
            return null;
        }

        return new ForRestMcpRunDetail(
            Summary: MapRunSummary(run),
            Response: MapResponse(run.Response),
            Responses: [.. run.Responses.Select(MapResponse).Where(static response => response is not null).Cast<ForRestMcpResponseView>()],
            Tests: [.. run.Tests.Select(MapTest)],
            Logs: [.. run.ConsoleEntries.Select(MapLog)],
            Stash: MapStash(run.Stash),
            RawRequest: run.RawRequest,
            SecretValues: CollectSecretValues(run.RuntimeVariables));
    }

    /// <summary>
    /// The secret values a run is known to have handled, so the MCP tools can scrub them from
    /// outgoing payloads (a script may have logged one, or a server may echo one back).
    /// </summary>
    private static IReadOnlyList<string> CollectSecretValues(IEnumerable<VariableDefinition> variables)
    {
        return
        [
            .. variables
                .Where(static variable => variable.IsSecret && !string.IsNullOrWhiteSpace(variable.Value))
                .Select(static variable => variable.Value)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    #endregion

    #region Settings

    public ForRestMcpServerSettingsView GetServerSettings()
    {
        ForRestMcpSettings settings = settingsProvider.GetCurrentSettings();
        return new ForRestMcpServerSettingsView(
            Enabled: settings.Enabled,
            BindAddress: settings.BindAddress,
            Port: settings.Port,
            HasAuthToken: !string.IsNullOrWhiteSpace(settings.AuthToken),
            MaxConcurrentSessions: settings.MaxConcurrentSessions,
            Endpoint: $"http://{settings.BindAddress}:{settings.Port}/");
    }

    public string GetSettingsText()
    {
        // The raw file holds the AI api_key, MCP auth_token, and custom
        // headers that may embed credentials. Never hand those to a
        // (possibly remote) MCP client verbatim.
        return McpSettingsRedactor.Redact(themeConfigStore.ReadAllText());
    }

    public ForRestMcpMutationResult UpdateSettingsText(string rawToml)
    {
        if (string.IsNullOrWhiteSpace(rawToml))
        {
            return ForRestMcpMutationResult.Fail("Settings content is required.");
        }

        try
        {
            // Re-parse to validate before writing so a malformed payload cannot
            // corrupt the live settings file.
            _ = themeConfigParser.Parse(rawToml);
        }
        catch (Exception exception)
        {
            return ForRestMcpMutationResult.Fail($"Settings rejected: {exception.Message}");
        }

        try
        {
            // The client only ever saw redacted secrets, so splice the original
            // secret values back in before persisting; a still-redacted value is
            // restored, a changed value is honored as a deliberate update.
            string original = themeConfigStore.ReadAllText();
            string restored = McpSettingsRedactor.Restore(original, rawToml);
            themeConfigStore.WriteAllText(restored);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to persist settings.toml from MCP.");
            return ForRestMcpMutationResult.Fail($"Failed to persist settings: {exception.Message}");
        }

        return ForRestMcpMutationResult.Ok("Settings saved. Restart the MCP server for bind/port changes to take effect.");
    }

    #endregion

    #region State helpers

    private Task<RequestWorkbenchState> LoadState(CancellationToken cancellationToken)
    {
        return stateStore.LoadAsync(NoDefaults, cancellationToken);
    }

    /// <summary>
    /// Loads the persisted state under the mutation gate, runs <paramref name="apply"/>,
    /// and saves the returned state when <c>Persist</c> is true. Callers always
    /// return the (possibly replaced) state record so a single, unambiguous
    /// signature handles both list mutations and record replacement.
    /// </summary>
    private async Task<TResult> Mutate<TResult>(
        CancellationToken cancellationToken,
        Func<RequestWorkbenchState, (bool Persist, TResult Result, RequestWorkbenchState State)> apply)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            RequestWorkbenchState state = await LoadState(cancellationToken);
            (bool persist, TResult result, RequestWorkbenchState updated) = apply(state);
            if (persist)
            {
                await stateStore.SaveAsync(updated, cancellationToken);
            }

            return result;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private static RequestWorkbenchWorkspaceState? SelectedWorkspace(RequestWorkbenchState state)
    {
        return state.Workspaces.FirstOrDefault(workspace => workspace.Id == state.SelectedWorkspaceId)
            ?? state.Workspaces.FirstOrDefault();
    }

    private static RequestWorkbenchDocumentState? SelectedDocument(RequestWorkbenchWorkspaceState? workspace)
    {
        if (workspace is null)
        {
            return null;
        }

        return workspace.Documents.FirstOrDefault(
                document => string.Equals(document.Location, workspace.SelectedDocumentLocation, StringComparison.OrdinalIgnoreCase))
            ?? workspace.Documents.FirstOrDefault();
    }

    private static RequestWorkbenchWorkspaceState? FindWorkspace(RequestWorkbenchState state, string workspaceId)
    {
        if (!Guid.TryParse(workspaceId, out Guid id))
        {
            return null;
        }

        return state.Workspaces.FirstOrDefault(workspace => workspace.Id == id);
    }

    private static RequestWorkbenchDocumentState? FindDocument(RequestWorkbenchWorkspaceState? workspace, string location)
    {
        if (workspace is null || string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        return workspace.Documents.FirstOrDefault(
            document => string.Equals(document.Location, location, StringComparison.OrdinalIgnoreCase));
    }

    private static void ReplaceDocument(
        RequestWorkbenchWorkspaceState workspace,
        RequestWorkbenchDocumentState existing,
        RequestWorkbenchDocumentState updated)
    {
        int index = workspace.Documents.FindIndex(
            document => string.Equals(document.Location, existing.Location, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            workspace.Documents[index] = updated;
        }
    }

    private RequestWorkbenchDocumentState UpdateSource(Guid workspaceId, RequestWorkbenchDocumentState document, string newSource)
    {
        (string method, string summary) = DescribeSource(workspaceId, newSource, document.Title);
        return document with
        {
            RequestSource = newSource,
            Method = method,
            Summary = summary,
        };
    }

    private (string Method, string Summary) DescribeSource(Guid workspaceId, string source, string name)
    {
        try
        {
            ForRestScriptCompilationResult compilation = scriptExecutionService.Compile(source, workspaceId, name);
            if (compilation.Payload is { } payload)
            {
                string method = payload.Request.Method.ToString().ToUpperInvariant();
                string summary = string.IsNullOrWhiteSpace(payload.Request.UrlTemplate)
                    ? string.Empty
                    : payload.Request.UrlTemplate;
                return (method, summary);
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not derive method/summary for MCP script '{Name}'.", name);
        }

        return ("GET", string.Empty);
    }

    private static string BuildLocation(IEnumerable<string> existingLocations, string name)
    {
        HashSet<string> taken = new(existingLocations, StringComparer.OrdinalIgnoreCase);
        string slug = Slugify(name);
        string baseLocation = $"/requests/{(string.IsNullOrEmpty(slug) ? "request" : slug)}";
        string candidate = baseLocation;
        int suffix = 2;
        while (taken.Contains(candidate))
        {
            candidate = $"{baseLocation}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string Slugify(string value)
    {
        StringBuilder builder = new(value.Length);
        bool previousDash = false;
        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousDash = false;
            }
            else if (!previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static Guid TryParseWorkspaceId(string workspaceId)
    {
        return Guid.TryParse(workspaceId, out Guid id) ? id : Guid.Empty;
    }

    private static ForRestMcpExecutionResult Failed(string message)
    {
        return new ForRestMcpExecutionResult(
            Succeeded: false,
            State: "Failed",
            Diagnostics: [],
            RunId: null,
            Response: null,
            Tests: [],
            Logs: [],
            Stash: null,
            ErrorMessage: message);
    }

    #endregion

    #region Mapping helpers

    private static ForRestMcpMutationResult MapLive(McpWorkbenchResult result)
    {
        return new ForRestMcpMutationResult(result.Succeeded, result.Message, result.Id);
    }

    private static ForRestMcpWorkspaceSummary MapLive(McpWorkbenchWorkspace workspace)
    {
        return new ForRestMcpWorkspaceSummary(
            workspace.Id,
            workspace.Name,
            [.. workspace.Scripts.Select(script => new ForRestMcpScriptSummary(script.Name, script.Method, script.UrlTemplate, script.Location))]);
    }

    private static ForRestMcpWorkspaceSummary MapWorkspace(RequestWorkbenchWorkspaceState workspace)
    {
        return new ForRestMcpWorkspaceSummary(
            Id: workspace.Id.ToString(),
            Name: workspace.Name,
            Scripts: [.. workspace.Documents.Select(static document => new ForRestMcpScriptSummary(
                Name: document.Title,
                Method: document.Method,
                UrlTemplate: document.Summary,
                Location: document.Location))]);
    }

    private static ForRestMcpCompileResult MapCompilation(ForRestScriptCompilationResult compilation)
    {
        ForRestExecutionPayload? payload = compilation.Payload;
        return new ForRestMcpCompileResult(
            Succeeded: compilation.Succeeded,
            Diagnostics: [.. compilation.Diagnostics.Select(static diagnostic => new ForRestMcpDiagnostic(
                Severity: diagnostic.Severity.ToString(),
                Message: diagnostic.Message,
                Line: diagnostic.Line,
                Column: diagnostic.Column))],
            Method: payload is null ? null : payload.Request.Method.ToString().ToUpperInvariant(),
            Url: payload?.Request.UrlTemplate,
            RequestName: payload?.Request.Name);
    }

    private static ForRestMcpResponseView? MapResponse(ResponseSnapshot? response)
    {
        if (response is null)
        {
            return null;
        }

        return new ForRestMcpResponseView(
            Status: response.StatusCode,
            ReasonPhrase: response.ReasonPhrase,
            ContentType: response.ContentType,
            SizeBytes: response.SizeBytes,
            DurationMilliseconds: response.DurationMilliseconds,
            Headers: [.. response.Headers.Select(static header => new ForRestMcpHeaderView(header.Key, header.Value))],
            Cookies: [.. response.Cookies.Select(static cookie => new ForRestMcpHeaderView(cookie.Key, cookie.Value))],
            Body: response.Body,
            RawResponse: response.RawResponse,
            Label: response.Label);
    }

    private static ForRestMcpTestView MapTest(TestResult test)
    {
        return new ForRestMcpTestView(test.Name, test.State.ToString(), test.Message);
    }

    private static ForRestMcpLogView MapLog(ConsoleEntry entry)
    {
        return new ForRestMcpLogView(entry.Level.ToString(), entry.Message, entry.TimestampUtc);
    }

    private static ForRestMcpRunSummary MapRunSummary(ExecutionRun run)
    {
        return new ForRestMcpRunSummary(
            Id: run.Id.ToString(),
            RequestName: run.RequestName,
            State: run.State.ToString(),
            Iteration: run.Iteration,
            StartedUtc: run.StartedUtc,
            CompletedUtc: run.CompletedUtc,
            TargetUri: run.TargetUri,
            Status: run.Response?.StatusCode,
            DurationMilliseconds: run.Response?.DurationMilliseconds,
            // Exception text can quote a secret (e.g. a failed auth header value); summaries go
            // straight to external agents via list_runs, so scrub here rather than in the tools.
            ErrorMessage: McpSecretValueScrubber.Scrub(run.ErrorMessage, CollectSecretValues(run.RuntimeVariables)));
    }

    private static ForRestMcpStashView? MapStash(StashTable? stash)
    {
        if (stash is null || (stash.Columns.Count == 0 && stash.Rows.Count == 0))
        {
            return null;
        }

        return new ForRestMcpStashView(
            Columns: [.. stash.Columns],
            Rows: [.. stash.Rows.Select(static row => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(row.Values))]);
    }

    #endregion

    #region Browser automation

    public bool BrowserAvailable => browserProvider.Current.IsAvailable;

    public async Task<string> BrowserNavigate(string url, CancellationToken cancellationToken)
    {
        await browserProvider.Current.Navigate(url, cancellationToken);
        return $"Navigated to {url}.";
    }

    public async Task<ForRestMcpBrowserSnapshotView> BrowserSnapshot(CancellationToken cancellationToken)
    {
        IBrowserAutomationBridge bridge = browserProvider.Current;
        if (!bridge.IsAvailable)
        {
            return new ForRestMcpBrowserSnapshotView(false, string.Empty, string.Empty, []);
        }

        BrowserSnapshot snapshot = await bridge.Snapshot(cancellationToken);
        return new ForRestMcpBrowserSnapshotView(
            true,
            snapshot.Url,
            snapshot.Title,
            [.. snapshot.Elements.Select(MapBrowserElement)]);
    }

    public Task<string> BrowserScreenshot(CancellationToken cancellationToken) =>
        browserProvider.Current.Screenshot(cancellationToken);

    public async Task<ForRestMcpBrowserElementView> BrowserQuery(string target, CancellationToken cancellationToken)
    {
        BrowserElementInfo info = await browserProvider.Current.Query(BrowserTarget.Parse(target), cancellationToken);
        return MapBrowserElement(info);
    }

    public async Task<string> BrowserClick(string target, CancellationToken cancellationToken)
    {
        await browserProvider.Current.Click(BrowserTarget.Parse(target), cancellationToken: cancellationToken);
        return $"Clicked {target}.";
    }

    public async Task<string> BrowserType(string target, string text, CancellationToken cancellationToken)
    {
        await browserProvider.Current.Type(BrowserTarget.Parse(target), text, cancellationToken: cancellationToken);
        return $"Typed into {target}.";
    }

    public async Task<string> BrowserPress(string keys, CancellationToken cancellationToken)
    {
        await browserProvider.Current.Press(keys, cancellationToken);
        return $"Pressed {keys}.";
    }

    public async Task<ForRestMcpBrowserElementView> BrowserWaitFor(string target, int timeoutMs, CancellationToken cancellationToken)
    {
        BrowserElementInfo info = await browserProvider.Current.WaitFor(BrowserTarget.Parse(target), timeoutMs, cancellationToken);
        return MapBrowserElement(info);
    }

    public Task<string> BrowserEvaluate(string expression, CancellationToken cancellationToken) =>
        browserProvider.Current.Evaluate(expression, cancellationToken);

    public async Task<ForRestMcpMutationResult> ShowInApp(string target, string? id, CancellationToken cancellationToken)
    {
        if (Live is not { } live)
        {
            return ForRestMcpMutationResult.Fail("Showing a pane requires the For-Rest desktop app to be running.");
        }

        McpWorkbenchResult result = await live.ShowInApp(target, id);
        return MapLive(result);
    }

    private static ForRestMcpBrowserElementView MapBrowserElement(BrowserElementInfo info) => new(
        info.Found,
        info.Tag,
        info.Id,
        info.Text,
        info.Css,
        info.Xpath,
        info.Role,
        info.Name,
        info.X,
        info.Y,
        info.Width,
        info.Height);

    #endregion
}
#endif
