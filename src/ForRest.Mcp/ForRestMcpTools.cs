using System.ComponentModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ForRest.Models;
using ForRest.Scripting;
using ForRest.Services.AI;

namespace ForRest.Mcp;

/// <summary>
/// Concrete tool implementations exposed by the For-Rest MCP server. Tools are
/// plain instance methods decorated with <see cref="DescriptionAttribute"/> so
/// the MCP SDK's reflection-based registration (via <c>McpServerTool.Create</c>)
/// can pick up parameter descriptions and return types without hand-written
/// JSON Schema.
///
/// The tools fall into three bands:
/// <list type="bullet">
/// <item>Passive tools (language reference, docs search, provider list, payloads,
/// attack-script generation) work with no <see cref="IForRestMcpHost"/> wired —
/// they only read the local scripting catalog, AI knowledge corpus, and payload
/// library.</item>
/// <item>Workbench tools (workspace/script CRUD, active document, compile,
/// settings) reach the live persisted workbench through the host.</item>
/// <item>Execution and results tools (run a script, list/inspect runs, grep a
/// response, read the stash, read debug logs) drive the same execution pipeline
/// and history store the desktop UI uses.</item>
/// </list>
/// Host-backed tools gracefully degrade to an informative error string when no
/// host is registered so external agents can still introspect the server.
/// Source text returned to clients has secret values redacted.
/// </summary>
public sealed class ForRestMcpTools
{
    #region Private Fields

    private readonly IAiKnowledgeCatalog knowledgeCatalog;
    private readonly IAiDocumentationSearchService documentationSearch;
    private readonly Func<IForRestMcpHost?> hostAccessor;

    private const string NoHostMessage =
        "No desktop canvas host is wired to this MCP server. Workbench, execution, and results tools require the For-Rest desktop app to be running.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    #endregion

    #region Constructors

    public ForRestMcpTools(
        IAiKnowledgeCatalog knowledgeCatalog,
        IAiDocumentationSearchService documentationSearch,
        Func<IForRestMcpHost?> hostAccessor)
    {
        this.knowledgeCatalog = knowledgeCatalog;
        this.documentationSearch = documentationSearch;
        this.hostAccessor = hostAccessor;
    }

    #endregion

    #region Passive tools

    [Description("Returns the canonical ForRest scripting language reference as markdown. Use this to learn the DSL (request surface, flow, auth modes, retry, delay, payloads, expect) before authoring or editing scripts.")]
    public string get_instructions()
    {
        return ForRestLanguageCatalog.BuildMarkdownReference();
    }

    [Description("Searches the local ForRest documentation corpus and returns the top matching entries with their content. Use this before guessing about language or app behavior.")]
    public string search_docs(
        [Description("Natural-language query describing what you want to know about For-Rest.")] string query,
        [Description("Maximum number of matching entries to return. Defaults to 5, capped at 20.")] int max_results = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "No query supplied.";
        }

        int limit = Math.Clamp(max_results, 1, 20);
        IReadOnlyList<AiKnowledgeSearchHit> hits = documentationSearch.Search(query, limit);
        if (hits.Count == 0)
        {
            return $"No documents matched '{query}'.";
        }

        return Serialize(hits.Select(hit => new
        {
            id = hit.Document.Id,
            title = hit.Document.Title,
            summary = hit.Document.Summary,
            source = hit.Document.SourcePath,
            score = hit.Score,
            excerpt = hit.Excerpt,
            content = hit.Document.Content,
        }));
    }

    [Description("Lists every AI provider preset For-Rest ships out of the box, with default endpoints, transports, and aliases.")]
    public string list_ai_providers()
    {
        AiKnowledgeDocument? providerDoc = knowledgeCatalog
            .GetDocuments()
            .FirstOrDefault(static doc => doc.Id == "forrest-ai-providers");

        return providerDoc?.Content ?? "AI provider reference is not registered.";
    }

    [Description("Lists every built-in security test payload category exposed to ForRest scripts via the `payloads` namespace, along with the number of payloads in each.")]
    public string list_payload_categories()
    {
        PayloadsApi payloads = new();
        IReadOnlyList<string> categories = payloads.Categories();
        return Serialize(categories.Select(category => new
        {
            name = category,
            count = payloads.Category(category).Count,
        }));
    }

    [Description("Returns the raw payload list for a given category (sqli, xss, path_traversal, command_injection, ssti, open_redirect, xxe, nosqli, crlf, ssrf, or any custom category).")]
    public string get_payloads(
        [Description("Category name or alias.")] string category)
    {
        PayloadsApi payloads = new();
        IReadOnlyList<string> list = payloads.Category(category);
        if (list.Count == 0)
        {
            return $"No payloads are registered for category '{category}'.";
        }

        return Serialize(list);
    }

    [Description("Generates a ready-to-run ForRest attack/fuzz script that iterates a built-in payload category against a target, sends one request per payload, and stashes the status, size, and payload for each attempt. Run the returned script with execute_script. Use this for authorized security testing only.")]
    public string build_attack_script(
        [Description("Absolute base target URL, e.g. https://api.example.test/items. For 'query' injection the payload is appended as a query value; for 'url_path' it is appended to the path; for 'body'/'header' the base URL is used as-is.")] string target_url,
        [Description("Payload category: sqli, xss, path_traversal, command_injection, ssti, open_redirect, xxe, nosqli, crlf, ssrf, or a custom category name.")] string category,
        [Description("Where to inject the payload: 'query' (default), 'url_path', 'body', or 'header'.")] string injection = "query",
        [Description("HTTP method. Defaults to GET (or POST when injection is 'body').")] string method = "",
        [Description("Parameter or header name to inject into for 'query' and 'header' injection. Defaults to 'q' (query) or 'X-Fuzz' (header).")] string parameter = "")
    {
        if (string.IsNullOrWhiteSpace(target_url))
        {
            return "target_url is required.";
        }

        PayloadsApi payloads = new();
        IReadOnlyList<string> list = payloads.Category(category);
        if (list.Count == 0)
        {
            return $"No payloads are registered for category '{category}'.";
        }

        string mode = injection.Trim().ToLowerInvariant();
        string resolvedMethod = string.IsNullOrWhiteSpace(method)
            ? (mode == "body" ? "POST" : "GET")
            : method.Trim().ToUpperInvariant();
        int iterations = list.Count + 5;
        string categoryLiteral = EscapeForRestString(category.Trim().ToLowerInvariant());
        string baseUrlLiteral = EscapeForRestString(target_url.Trim());

        StringBuilder builder = new();
        builder.AppendLine("request {");
        builder.AppendLine($"  method = {resolvedMethod}");
        builder.AppendLine($"  url = \"{baseUrlLiteral}\"");
        if (mode == "body")
        {
            builder.AppendLine("  content_type = \"application/json\"");
        }

        builder.AppendLine($"  max_send_iterations = {iterations}");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("flow {");
        builder.AppendLine($"  foreach payload in payloads.Category(\"{categoryLiteral}\") {{");

        switch (mode)
        {
            case "url_path":
                builder.AppendLine($"    request.url = $\"{baseUrlLiteral}/{{encoding.UrlEncode(payload)}}\"");
                break;
            case "body":
                builder.AppendLine("    request.content_type = \"application/json\"");
                builder.AppendLine("    request.body = json.Stringify(new { value = payload })");
                break;
            case "header":
                string headerName = EscapeForRestString(string.IsNullOrWhiteSpace(parameter) ? "X-Fuzz" : parameter.Trim());
                builder.AppendLine($"    request.headers[\"{headerName}\"] = payload");
                break;
            default: // query
                string queryName = EscapeForRestString(string.IsNullOrWhiteSpace(parameter) ? "q" : parameter.Trim());
                string separator = target_url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                builder.AppendLine($"    request.url = $\"{baseUrlLiteral}{separator}{queryName}={{encoding.UrlEncode(payload)}}\"");
                break;
        }

        builder.AppendLine("    let sent = request.send()");
        builder.AppendLine("    stash.Payload = payload");
        builder.AppendLine("    stash.Status = sent.status");
        builder.AppendLine("    stash.Commit()");
        builder.AppendLine("  }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    [Description("Diffs and fingerprints two HTTP responses (a baseline and a candidate) the same way the ForRest `fuzz` engine does, and reports any anomalies: status changes, large size deltas, and time-based anomalies (the classic blind-injection signal). Pure analysis — no host or network required. Use it to triage whether a fuzz attempt's response is meaningfully different from a clean baseline.")]
    public string analyze_responses(
        [Description("Baseline (known-good) response status code.")] int baseline_status,
        [Description("Baseline response body size in bytes.")] long baseline_size_bytes,
        [Description("Baseline response duration in milliseconds.")] long baseline_duration_ms,
        [Description("Candidate response status code.")] int candidate_status,
        [Description("Candidate response body size in bytes.")] long candidate_size_bytes,
        [Description("Candidate response duration in milliseconds.")] long candidate_duration_ms)
    {
        FuzzApi fuzz = new(new ConsoleApi());

        ResponseSnapshot baselineResponse = new()
        {
            StatusCode = baseline_status,
            SizeBytes = baseline_size_bytes,
            DurationMilliseconds = baseline_duration_ms,
        };
        ResponseSnapshot candidateResponse = new()
        {
            StatusCode = candidate_status,
            SizeBytes = candidate_size_bytes,
            DurationMilliseconds = candidate_duration_ms,
        };

        FuzzBaseline baseline = fuzz.Baseline(baselineResponse);
        FuzzFingerprint candidateFingerprint = fuzz.Fingerprint(candidateResponse);
        FuzzDiff diff = fuzz.Diff(baseline, candidateResponse);

        return Serialize(new
        {
            baseline = new
            {
                status = baseline.Fingerprint.Status,
                sizeBytes = baseline.Fingerprint.SizeBytes,
                sizeBucket = baseline.Fingerprint.SizeBucket,
                durationMilliseconds = baseline.Fingerprint.DurationMilliseconds,
                clusterKey = baseline.Fingerprint.ClusterKey(),
            },
            candidate = new
            {
                status = candidateFingerprint.Status,
                sizeBytes = candidateFingerprint.SizeBytes,
                sizeBucket = candidateFingerprint.SizeBucket,
                durationMilliseconds = candidateFingerprint.DurationMilliseconds,
                clusterKey = candidateFingerprint.ClusterKey(),
            },
            isAnomalous = diff.IsAnomalous,
            anomalies = diff.Anomalies,
        });
    }

    [Description("Generates a ready-to-run ForRest script that uses the new `fuzz` engine: it iterates a built-in payload category against a target with bounded concurrency, captures a baseline, diffs every response, and stashes the flagged anomalies. Optionally constrains the run to an in-scope host allowlist. Run the returned script with execute_script. Authorized security testing only.")]
    public string build_fuzz_script(
        [Description("Absolute base target URL, e.g. https://api.example.test/search.")] string target_url,
        [Description("Payload category: sqli, xss, path_traversal, command_injection, ssti, open_redirect, xxe, nosqli, crlf, ssrf, ldap, header_injection, prototype_pollution, or a custom category name.")] string category,
        [Description("Where to inject the payload: 'query' (default), 'url_path', 'body', or 'header'.")] string injection = "query",
        [Description("Parameter or header name to inject into for 'query' and 'header' injection. Defaults to 'q' (query) or 'X-Fuzz' (header).")] string parameter = "",
        [Description("Maximum concurrent attempts. Defaults to 4.")] int max_concurrency = 4,
        [Description("Per-attempt timeout in milliseconds. Defaults to 8000.")] int timeout_ms = 8000,
        [Description("Delay before each send in milliseconds (courteous rate limiting). Defaults to 100.")] int delay_ms = 100,
        [Description("Restrict the run to the target's host via fuzz.AllowHost(). Defaults to true.")] bool restrict_to_host = true)
    {
        if (string.IsNullOrWhiteSpace(target_url))
        {
            return "target_url is required.";
        }

        PayloadsApi payloads = new();
        IReadOnlyList<string> list = payloads.Category(category);
        if (list.Count == 0)
        {
            return $"No payloads are registered for category '{category}'.";
        }

        string mode = injection.Trim().ToLowerInvariant();
        string resolvedMethod = mode == "body" ? "POST" : "GET";
        int iterations = list.Count + 5;
        string categoryLiteral = EscapeForRestString(category.Trim().ToLowerInvariant());
        string baseUrlLiteral = EscapeForRestString(target_url.Trim());
        string host = Uri.TryCreate(target_url.Trim(), UriKind.Absolute, out Uri? uri) ? uri.Host : target_url.Trim();
        int safeConcurrency = Math.Clamp(max_concurrency, 1, 64);
        int safeTimeout = Math.Max(0, timeout_ms);
        int safeDelay = Math.Max(0, delay_ms);

        StringBuilder builder = new();
        builder.AppendLine("request {");
        builder.AppendLine($"  method = {resolvedMethod}");
        builder.AppendLine($"  url = \"{baseUrlLiteral}\"");
        if (mode == "body")
        {
            builder.AppendLine("  content_type = \"application/json\"");
        }

        builder.AppendLine($"  max_send_iterations = {iterations}");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("flow {");
        if (restrict_to_host)
        {
            builder.AppendLine($"  fuzz.AllowHost(\"{EscapeForRestString(host)}\")   # authorized scope only");
        }

        builder.AppendLine($"  let opts = new ForRest.Scripting.FuzzOptions {{ MaxConcurrency = {safeConcurrency}, TimeoutMs = {safeTimeout}, DelayMs = {safeDelay} }}");
        builder.AppendLine($"  let result = await fuzz.Run(payloads.Category(\"{categoryLiteral}\"), async (string payload) => {{");

        switch (mode)
        {
            case "url_path":
                builder.AppendLine($"    request.url = $\"{baseUrlLiteral}/{{encoding.UrlEncode(payload)}}\"");
                break;
            case "body":
                builder.AppendLine("    request.content_type = \"application/json\"");
                builder.AppendLine("    request.body = json.Stringify(new { value = payload })");
                break;
            case "header":
                string headerName = EscapeForRestString(string.IsNullOrWhiteSpace(parameter) ? "X-Fuzz" : parameter.Trim());
                builder.AppendLine($"    request.headers[\"{headerName}\"] = payload");
                break;
            default: // query
                string queryName = EscapeForRestString(string.IsNullOrWhiteSpace(parameter) ? "q" : parameter.Trim());
                string separator = target_url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                builder.AppendLine($"    request.url = $\"{baseUrlLiteral}{separator}{queryName}={{encoding.UrlEncode(payload)}}\"");
                break;
        }

        builder.AppendLine("    let sent = await request.send()");
        builder.AppendLine("    return sent.Snapshot");
        builder.AppendLine($"  }}, opts, \"{categoryLiteral}\")");
        builder.AppendLine();
        builder.AppendLine("  console.Log(result.Summarize())");
        builder.AppendLine("  foreach f in result.Findings {");
        builder.AppendLine("    stash.Payload = f.Payload");
        builder.AppendLine("    stash.Anomalies = strings.Join(\"; \", f.Anomalies)");
        builder.AppendLine("    stash.Commit()");
        builder.AppendLine("  }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    #endregion

    #region Active document

    [Description("Returns a JSON snapshot of the active ForRest request document (the selected document in the selected workspace): id, title, language, workspace, and full source text. Secret values are redacted.")]
    public Task<string> get_active_request()
    {
        return WithHostAsync(async host =>
        {
            ForRestMcpDocumentSnapshot? snapshot = await host.GetActiveDocument(CancellationToken.None);
            if (snapshot is null)
            {
                return "No active document is open.";
            }

            return Serialize(new
            {
                id = snapshot.DocumentId,
                title = snapshot.Title,
                language = snapshot.Language,
                workspace = snapshot.WorkspaceName,
                source = McpSecretRedactor.Redact(snapshot.SourceText),
            });
        });
    }

    [Description("Replaces the entire source of the active ForRest request document with the supplied text. The host restores any redacted secret declarations before saving. The new source should be valid ForRest script.")]
    public Task<string> replace_active_request(
        [Description("Full ForRest script to install as the new active document body.")] string new_source)
    {
        if (string.IsNullOrWhiteSpace(new_source))
        {
            return Task.FromResult("new_source must not be empty.");
        }

        return WithHostAsync(async host =>
        {
            ForRestMcpUpdateResult result = await host.ReplaceActiveDocument(new_source, CancellationToken.None);
            return result.Succeeded
                ? "Active document replaced."
                : $"Active document replace failed: {result.ErrorMessage}";
        });
    }

    [Description("Selects which document is active by workspace id and script location, so subsequent active-document tools target it.")]
    public Task<string> set_active_request(
        [Description("Workspace id (GUID) from list_workspaces.")] string workspace_id,
        [Description("Script location/path from list_workspaces (e.g. /requests/get-users).")] string location)
    {
        return WithHostAsync(async host => Describe(await host.SetActiveDocument(workspace_id, location, CancellationToken.None)));
    }

    #endregion

    #region Workspace CRUD

    [Description("Lists the workspaces in the live workbench, each with its scripts (name, HTTP method, URL template, location). Start here to orient before editing or executing.")]
    public Task<string> list_workspaces()
    {
        return WithHostAsync(async host => Serialize((await host.ListWorkspaces(CancellationToken.None)).Select(MapWorkspace)));
    }

    [Description("Returns a single workspace by id with its scripts.")]
    public Task<string> get_workspace(
        [Description("Workspace id (GUID).")] string workspace_id)
    {
        return WithHostAsync(async host =>
        {
            ForRestMcpWorkspaceSummary? workspace = await host.GetWorkspace(workspace_id, CancellationToken.None);
            return workspace is null
                ? $"No workspace found with id '{workspace_id}'."
                : Serialize(MapWorkspace(workspace));
        });
    }

    [Description("Creates a new empty workspace and returns its id.")]
    public Task<string> create_workspace(
        [Description("Display name for the new workspace.")] string name)
    {
        return WithHostAsync(async host => Describe(await host.CreateWorkspace(name, CancellationToken.None)));
    }

    [Description("Renames a workspace.")]
    public Task<string> rename_workspace(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("New display name.")] string new_name)
    {
        return WithHostAsync(async host => Describe(await host.RenameWorkspace(workspace_id, new_name, CancellationToken.None)));
    }

    [Description("Deletes a workspace and all of its scripts. This is destructive and cannot be undone.")]
    public Task<string> delete_workspace(
        [Description("Workspace id (GUID).")] string workspace_id)
    {
        return WithHostAsync(async host => Describe(await host.DeleteWorkspace(workspace_id, CancellationToken.None)));
    }

    #endregion

    #region Script CRUD

    [Description("Returns a single script's full detail (name, method, summary, source, pre-request script). Secret values in the source are redacted.")]
    public Task<string> get_script(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Script location/path from list_workspaces.")] string location)
    {
        return WithHostAsync(async host =>
        {
            ForRestMcpScriptDetail? script = await host.GetScript(workspace_id, location, CancellationToken.None);
            if (script is null)
            {
                return $"No script found at '{location}' in workspace '{workspace_id}'.";
            }

            return Serialize(new
            {
                workspaceId = script.WorkspaceId,
                workspaceName = script.WorkspaceName,
                location = script.Location,
                name = script.Name,
                method = script.Method,
                summary = script.Summary,
                source = McpSecretRedactor.Redact(script.Source),
                preRequestScript = script.PreRequestScript,
            });
        });
    }

    [Description("Creates a new request script in a workspace from ForRest source text and returns its location.")]
    public Task<string> create_script(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Display name for the new script.")] string name,
        [Description("Full ForRest script source.")] string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Task.FromResult("source must not be empty.");
        }

        return WithHostAsync(async host => Describe(await host.CreateScript(workspace_id, name, source, CancellationToken.None)));
    }

    [Description("Replaces the source of an existing script. The host restores any redacted secret declarations before saving.")]
    public Task<string> update_script(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Script location/path.")] string location,
        [Description("Full replacement ForRest script source.")] string new_source)
    {
        if (string.IsNullOrWhiteSpace(new_source))
        {
            return Task.FromResult("new_source must not be empty.");
        }

        return WithHostAsync(async host => Describe(await host.UpdateScript(workspace_id, location, new_source, CancellationToken.None)));
    }

    [Description("Renames an existing script.")]
    public Task<string> rename_script(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Script location/path.")] string location,
        [Description("New display name.")] string new_name)
    {
        return WithHostAsync(async host => Describe(await host.RenameScript(workspace_id, location, new_name, CancellationToken.None)));
    }

    [Description("Deletes a script from a workspace. This is destructive and cannot be undone.")]
    public Task<string> delete_script(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Script location/path.")] string location)
    {
        return WithHostAsync(async host => Describe(await host.DeleteScript(workspace_id, location, CancellationToken.None)));
    }

    #endregion

    #region Compile / execute

    [Description("Compiles a ForRest script without sending any request and returns diagnostics plus the resolved method, URL, and request name. Use this to validate edits before executing.")]
    public string compile_script(
        [Description("Workspace id (GUID) the script belongs to. Affects variable scoping.")] string workspace_id,
        [Description("Full ForRest script source to compile.")] string source)
    {
        return WithHost(host =>
        {
            ForRestMcpCompileResult result = host.CompileScript(workspace_id, source);
            return Serialize(new
            {
                succeeded = result.Succeeded,
                method = result.Method,
                url = result.Url,
                requestName = result.RequestName,
                diagnostics = result.Diagnostics.Select(MapDiagnostic),
            });
        });
    }

    [Description("Compiles and executes a ForRest script against the live HTTP pipeline (real network request) and returns the response, tests, debug logs, and stash. The run is saved to the workspace's history. Use for authorized testing only.")]
    public Task<string> execute_script(
        [Description("Workspace id (GUID) to execute under.")] string workspace_id,
        [Description("Full ForRest script source to execute.")] string source,
        [Description("Optional request name used in history and diagnostics.")] string request_name = "")
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Task.FromResult("source must not be empty.");
        }

        return WithHostAsync(async host =>
        {
            ForRestMcpExecutionResult result = await host.ExecuteScript(
                workspace_id,
                source,
                string.IsNullOrWhiteSpace(request_name) ? null : request_name,
                CancellationToken.None);
            return Serialize(MapExecution(result));
        });
    }

    [Description("Executes a stored script (by workspace id and location) against the live HTTP pipeline and returns the response, tests, debug logs, and stash. The run is saved to history.")]
    public Task<string> execute_stored_script(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Script location/path from list_workspaces.")] string location)
    {
        return WithHostAsync(async host =>
        {
            ForRestMcpExecutionResult result = await host.ExecuteStoredScript(workspace_id, location, CancellationToken.None);
            return Serialize(MapExecution(result));
        });
    }

    #endregion

    #region Results / monitoring

    [Description("Lists recent execution runs for a workspace (most recent first) with status, target, timing, and any error. Use this to monitor what has been executed.")]
    public Task<string> list_runs(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Maximum number of runs to return. Defaults to 20, capped at 200.")] int max_results = 20)
    {
        int limit = Math.Clamp(max_results, 1, 200);
        return WithHostAsync(async host => Serialize((await host.ListRuns(workspace_id, limit, CancellationToken.None)).Select(MapRunSummary)));
    }

    [Description("Returns the full detail of a single execution run: response (status, headers, cookies, body), all responses for multi-send flows, tests, debug logs, stash, and the raw request (credential-bearing header values are redacted).")]
    public Task<string> get_run(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Run id (GUID) from list_runs.")] string run_id)
    {
        return WithHostAsync(async host =>
        {
            ForRestMcpRunDetail? run = await host.GetRun(workspace_id, run_id, CancellationToken.None);
            if (run is null)
            {
                return $"No run found with id '{run_id}' in workspace '{workspace_id}'.";
            }

            return Serialize(new
            {
                summary = MapRunSummary(run.Summary),
                response = MapResponse(run.Response),
                responses = run.Responses.Select(response => MapResponse(response)),
                tests = run.Tests.Select(MapTest),
                logs = run.Logs.Select(MapLog),
                stash = MapStash(run.Stash),
                rawRequest = McpRawRequestRedactor.Redact(run.RawRequest),
            });
        });
    }

    [Description("Returns just the response of a run (status, headers, cookies, content type, size, timing, body). Optionally truncates the body to keep results compact.")]
    public Task<string> get_run_response(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Run id (GUID).")] string run_id,
        [Description("Maximum body characters to return. 0 (default) returns the full body; otherwise the body is truncated.")] int max_body_chars = 0)
    {
        return WithHostAsync(async host =>
        {
            ForRestMcpRunDetail? run = await host.GetRun(workspace_id, run_id, CancellationToken.None);
            if (run is null)
            {
                return $"No run found with id '{run_id}' in workspace '{workspace_id}'.";
            }

            if (run.Response is null)
            {
                return "This run produced no response.";
            }

            return Serialize(MapResponse(run.Response, max_body_chars)!);
        });
    }

    [Description("Searches (greps) a run's response body for a pattern and returns matching lines with line numbers. Supports substring or regex matching, case sensitivity, and optional surrounding context lines.")]
    public Task<string> grep_run_response(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Run id (GUID).")] string run_id,
        [Description("Search pattern (substring by default, or a .NET regex when is_regex is true).")] string pattern,
        [Description("Treat the pattern as a regular expression. Defaults to false.")] bool is_regex = false,
        [Description("Case-insensitive matching. Defaults to true.")] bool ignore_case = true,
        [Description("Maximum number of matches to return. Defaults to 50, capped at 500.")] int max_matches = 50,
        [Description("Number of context lines to include before and after each match. Defaults to 0.")] int context_lines = 0,
        [Description("Also search the raw response (status line + headers + body) instead of just the decoded body. Defaults to false.")] bool include_raw = false)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return Task.FromResult("pattern must not be empty.");
        }

        return WithHostAsync(async host =>
        {
            ForRestMcpRunDetail? run = await host.GetRun(workspace_id, run_id, CancellationToken.None);
            if (run is null)
            {
                return $"No run found with id '{run_id}' in workspace '{workspace_id}'.";
            }

            if (run.Response is null)
            {
                return "This run produced no response.";
            }

            string haystack = include_raw
                ? (string.IsNullOrEmpty(run.Response.RawResponse) ? run.Response.Body : run.Response.RawResponse)
                : run.Response.Body;

            return GrepText(haystack, pattern, is_regex, ignore_case, Math.Clamp(max_matches, 1, 500), Math.Max(0, context_lines));
        });
    }

    [Description("Returns the stash table captured by a run (columns and rows). The stash is how ForRest scripts record structured per-iteration results for batch probes and fuzz runs.")]
    public Task<string> get_run_stash(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Run id (GUID).")] string run_id)
    {
        return WithHostAsync(async host =>
        {
            ForRestMcpRunDetail? run = await host.GetRun(workspace_id, run_id, CancellationToken.None);
            if (run is null)
            {
                return $"No run found with id '{run_id}' in workspace '{workspace_id}'.";
            }

            object? stash = MapStash(run.Stash);
            return stash is null ? "This run captured no stash rows." : Serialize(stash);
        });
    }

    [Description("Returns a run's debug logs (console entries) and test assertion results — the equivalent of reading the debug pane. Useful for diagnosing failures.")]
    public Task<string> get_run_logs(
        [Description("Workspace id (GUID).")] string workspace_id,
        [Description("Run id (GUID).")] string run_id)
    {
        return WithHostAsync(async host =>
        {
            ForRestMcpRunDetail? run = await host.GetRun(workspace_id, run_id, CancellationToken.None);
            if (run is null)
            {
                return $"No run found with id '{run_id}' in workspace '{workspace_id}'.";
            }

            return Serialize(new
            {
                state = run.Summary.State,
                errorMessage = run.Summary.ErrorMessage,
                tests = run.Tests.Select(MapTest),
                logs = run.Logs.Select(MapLog),
            });
        });
    }

    #endregion

    #region Browser automation

    [Description("Navigates the embedded For-Rest browser pane to a URL. The desktop Browser tab must be open. This is the agent's 'hands': pair it with browser_snapshot/browser_query for 'eyes', then record the actions into a script for deterministic replay.")]
    public Task<string> browser_navigate(
        [Description("Absolute URL to load, e.g. https://app.example.test/login.")] string url)
    {
        return WithHostAsync(async host => await host.BrowserNavigate(url, CancellationToken.None));
    }

    [Description("Returns the page's interactive elements (links, buttons, inputs, role/aria nodes) with their stable selectors (css, xpath, role+name, id) and on-screen positions. Use this as the agent's 'eyes' to decide what to target next.")]
    public Task<string> browser_snapshot()
    {
        return WithHostAsync(async host => Serialize(await host.BrowserSnapshot(CancellationToken.None)));
    }

    [Description("Captures a screenshot of the current page and returns it as a base64-encoded PNG.")]
    public Task<string> browser_screenshot()
    {
        return WithHostAsync(async host => await host.BrowserScreenshot(CancellationToken.None));
    }

    [Description("Locates a single element and returns its stable selectors (css, xpath, role+name, id), text, and bounding box. Use the returned selectors when authoring or recording a replayable script. Target forms: '#id', 'css=.row', 'xpath=//a', 'text=Save', 'role=button:Save', 'testid=submit'.")]
    public Task<string> browser_query(
        [Description("Element target expression, e.g. '#submit' or 'role=button:Save'.")] string target)
    {
        return WithHostAsync(async host => Serialize(await host.BrowserQuery(target, CancellationToken.None)));
    }

    [Description("Clicks the element matching the target, moving the visible cursor to it first. Target forms: '#id', 'css=.row', 'xpath=//a', 'text=Save', 'role=button:Save', 'testid=submit'.")]
    public Task<string> browser_click(
        [Description("Element target expression.")] string target)
    {
        return WithHostAsync(async host => await host.BrowserClick(target, CancellationToken.None));
    }

    [Description("Types text into the element matching the target (clicks to focus first).")]
    public Task<string> browser_type(
        [Description("Element target expression.")] string target,
        [Description("Text to type.")] string text)
    {
        return WithHostAsync(async host => await host.BrowserType(target, text, CancellationToken.None));
    }

    [Description("Presses a key or key name in the page (e.g. 'Enter', 'Tab', 'Escape', 'ArrowDown', or a single character).")]
    public Task<string> browser_press(
        [Description("Key name or single character.")] string keys)
    {
        return WithHostAsync(async host => await host.BrowserPress(keys, CancellationToken.None));
    }

    [Description("Waits up to a timeout for an element matching the target to appear, then returns its selectors and position. Use before interacting with elements that load asynchronously.")]
    public Task<string> browser_wait_for(
        [Description("Element target expression.")] string target,
        [Description("Maximum time to wait in milliseconds (default 5000).")] int timeout_ms = 5000)
    {
        return WithHostAsync(async host => Serialize(await host.BrowserWaitFor(target, timeout_ms, CancellationToken.None)));
    }

    [Description("Evaluates a JavaScript expression in the current page and returns the result as JSON. Use for reads the other tools do not cover.")]
    public Task<string> browser_eval(
        [Description("JavaScript expression to evaluate in the page.")] string expression)
    {
        return WithHostAsync(async host => await host.BrowserEvaluate(expression, CancellationToken.None));
    }

    #endregion

    #region App surface

    [Description("Focuses a pane/tab in the running For-Rest desktop app so the user looks at it before you respond — making the experience feel seamless (e.g. flip to the live browser, or show the response after a run). Center targets: 'browser', 'request'. Inspector targets: 'response', 'requests', 'stash', 'headers', 'trace', 'raw', 'debug'. To open a specific document instead, use set_active_request. Requires the desktop app to be running.")]
    public Task<string> show_in_app(
        [Description("Which pane/tab to focus: browser, request, response, requests, stash, headers, trace, raw, or debug.")] string target,
        [Description("Reserved for future targets; leave empty.")] string id = "")
    {
        return WithHostAsync(async host => Describe(await host.ShowInApp(target, string.IsNullOrWhiteSpace(id) ? null : id, CancellationToken.None)));
    }

    #endregion

    #region Settings

    [Description("Returns the current For-Rest MCP server settings (enabled, bind address, port, whether an auth token is set, max concurrent sessions, and the active endpoint). The token value itself is never returned.")]
    public string get_server_settings()
    {
        return WithHost(host =>
        {
            ForRestMcpServerSettingsView settings = host.GetServerSettings();
            return Serialize(new
            {
                enabled = settings.Enabled,
                bindAddress = settings.BindAddress,
                port = settings.Port,
                hasAuthToken = settings.HasAuthToken,
                maxConcurrentSessions = settings.MaxConcurrentSessions,
                endpoint = settings.Endpoint,
            });
        });
    }

    [Description("Returns the settings.toml content for the For-Rest app so an agent can inspect theme, AI, and MCP configuration before proposing changes. Secret values (api keys, the MCP auth token, the license key, and custom headers) are redacted to \"***\".")]
    public string get_settings_text()
    {
        return WithHost(host =>
        {
            string text = host.GetSettingsText();
            return string.IsNullOrWhiteSpace(text) ? "Settings file is empty or not yet created." : text;
        });
    }

    [Description("Overwrites the settings.toml content. The host validates the supplied TOML before persisting. Any secret you leave as the redacted \"***\" marker keeps its original value; set a real value to change a secret. Note: changes to the MCP bind address or port only take effect after the server restarts.")]
    public string update_settings_text(
        [Description("Full replacement settings.toml content.")] string raw_toml)
    {
        if (string.IsNullOrWhiteSpace(raw_toml))
        {
            return "raw_toml must not be empty.";
        }

        return WithHost(host => Describe(host.UpdateSettingsText(raw_toml)));
    }

    #endregion

    #region Mapping helpers

    private static object MapWorkspace(ForRestMcpWorkspaceSummary workspace) => new
    {
        id = workspace.Id,
        name = workspace.Name,
        scripts = workspace.Scripts.Select(static script => new
        {
            name = script.Name,
            method = script.Method,
            location = script.Location,
            url_template = script.UrlTemplate,
        }),
    };

    private static object MapDiagnostic(ForRestMcpDiagnostic diagnostic) => new
    {
        severity = diagnostic.Severity,
        message = diagnostic.Message,
        line = diagnostic.Line,
        column = diagnostic.Column,
    };

    private static object MapExecution(ForRestMcpExecutionResult result) => new
    {
        succeeded = result.Succeeded,
        state = result.State,
        runId = result.RunId,
        errorMessage = result.ErrorMessage,
        diagnostics = result.Diagnostics.Select(MapDiagnostic),
        response = MapResponse(result.Response),
        tests = result.Tests.Select(MapTest),
        logs = result.Logs.Select(MapLog),
        stash = MapStash(result.Stash),
    };

    private static object? MapResponse(ForRestMcpResponseView? response, int maxBodyChars = 0)
    {
        if (response is null)
        {
            return null;
        }

        string body = response.Body;
        bool truncated = false;
        if (maxBodyChars > 0 && body.Length > maxBodyChars)
        {
            body = body[..maxBodyChars];
            truncated = true;
        }

        return new
        {
            status = response.Status,
            reasonPhrase = response.ReasonPhrase,
            contentType = response.ContentType,
            sizeBytes = response.SizeBytes,
            durationMilliseconds = response.DurationMilliseconds,
            label = response.Label,
            headers = response.Headers.Select(static header => new { key = header.Key, value = header.Value }),
            cookies = response.Cookies.Select(static cookie => new { key = cookie.Key, value = cookie.Value }),
            body,
            bodyTruncated = truncated,
        };
    }

    private static object MapTest(ForRestMcpTestView test) => new
    {
        name = test.Name,
        state = test.State,
        message = test.Message,
    };

    private static object MapLog(ForRestMcpLogView log) => new
    {
        level = log.Level,
        message = log.Message,
        timestampUtc = log.TimestampUtc,
    };

    private static object MapRunSummary(ForRestMcpRunSummary run) => new
    {
        id = run.Id,
        requestName = run.RequestName,
        state = run.State,
        iteration = run.Iteration,
        startedUtc = run.StartedUtc,
        completedUtc = run.CompletedUtc,
        targetUri = run.TargetUri,
        status = run.Status,
        durationMilliseconds = run.DurationMilliseconds,
        errorMessage = run.ErrorMessage,
    };

    private static object? MapStash(ForRestMcpStashView? stash)
    {
        if (stash is null || (stash.Columns.Count == 0 && stash.Rows.Count == 0))
        {
            return null;
        }

        return new
        {
            columns = stash.Columns,
            rows = stash.Rows,
        };
    }

    #endregion

    #region Private Methods

    private string WithHost(Func<IForRestMcpHost, string> action)
    {
        IForRestMcpHost? host = hostAccessor();
        return host is null ? NoHostMessage : action(host);
    }

    private async Task<string> WithHostAsync(Func<IForRestMcpHost, Task<string>> action)
    {
        IForRestMcpHost? host = hostAccessor();
        return host is null ? NoHostMessage : await action(host);
    }

    private static string Describe(ForRestMcpMutationResult result)
    {
        return Serialize(new
        {
            succeeded = result.Succeeded,
            message = result.Message,
            id = result.Id,
        });
    }

    private static string GrepText(string haystack, string pattern, bool isRegex, bool ignoreCase, int maxMatches, int contextLines)
    {
        if (string.IsNullOrEmpty(haystack))
        {
            return "Nothing to search (response body is empty).";
        }

        string[] lines = haystack.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        Func<string, bool> isMatch;
        if (isRegex)
        {
            Regex regex;
            try
            {
                regex = new Regex(pattern, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            }
            catch (ArgumentException exception)
            {
                return $"Invalid regular expression: {exception.Message}";
            }

            isMatch = line => regex.IsMatch(line);
        }
        else
        {
            StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            isMatch = line => line.Contains(pattern, comparison);
        }

        List<object> matches = [];
        int total = 0;
        for (int i = 0; i < lines.Length && matches.Count < maxMatches; i++)
        {
            if (!isMatch(lines[i]))
            {
                continue;
            }

            total++;
            int start = Math.Max(0, i - contextLines);
            int end = Math.Min(lines.Length - 1, i + contextLines);
            List<object> window = [];
            for (int j = start; j <= end; j++)
            {
                window.Add(new { line = j + 1, text = lines[j], isMatch = j == i });
            }

            matches.Add(new { line = i + 1, text = lines[i], context = contextLines > 0 ? window : null });
        }

        return Serialize(new
        {
            pattern,
            isRegex,
            ignoreCase,
            totalMatches = total,
            truncated = matches.Count >= maxMatches,
            matches,
        });
    }

    private static string EscapeForRestString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    #endregion
}
