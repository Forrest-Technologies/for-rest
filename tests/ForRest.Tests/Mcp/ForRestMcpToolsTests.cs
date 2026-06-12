using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ForRest.Mcp;
using ForRest.Services.AI;

namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class ForRestMcpToolsTests
{
    #region Passive tools

    [TestMethod]
    public void Get_instructions_returns_markdown_language_reference()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string instructions = tools.get_instructions();

        StringAssert.Contains(instructions, "ForRest Script Language");
        StringAssert.Contains(instructions, "## AI");
    }

    [TestMethod]
    public void Search_docs_returns_hits_for_language_keywords()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string json = tools.search_docs("retry with backoff", max_results: 5);

        Assert.IsFalse(json.StartsWith("No ", System.StringComparison.Ordinal));
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.Array, parsed.RootElement.ValueKind);
        Assert.IsTrue(parsed.RootElement.GetArrayLength() > 0);
    }

    [TestMethod]
    public void List_ai_providers_surfaces_knowledge_entry_when_registered()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string result = tools.list_ai_providers();

        StringAssert.Contains(result, "AI providers");
        StringAssert.Contains(result, "openai");
        StringAssert.Contains(result, "grok");
    }

    [TestMethod]
    public void List_payload_categories_lists_all_builtin_entries()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string json = tools.list_payload_categories();

        StringAssert.Contains(json, "sqli");
        StringAssert.Contains(json, "xss");
        StringAssert.Contains(json, "ssrf");
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.Array, parsed.RootElement.ValueKind);
    }

    [TestMethod]
    public void Get_payloads_returns_payloads_for_valid_category()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string json = tools.get_payloads("sqli");

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.Array, parsed.RootElement.ValueKind);
        Assert.IsTrue(parsed.RootElement.GetArrayLength() > 0);
    }

    [TestMethod]
    public void Get_payloads_reports_unknown_category_as_plain_text()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string result = tools.get_payloads("no-such-category");

        StringAssert.Contains(result, "No payloads");
    }

    [TestMethod]
    public void Build_attack_script_emits_runnable_query_fuzz_harness()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string script = tools.build_attack_script("https://target.test/items", "sqli");

        StringAssert.Contains(script, "request {");
        StringAssert.Contains(script, "flow {");
        StringAssert.Contains(script, "foreach payload in payloads.Category(\"sqli\")");
        StringAssert.Contains(script, "request.send()");
        StringAssert.Contains(script, "stash.Commit()");
        StringAssert.Contains(script, "?q=");
    }

    [TestMethod]
    public void Build_attack_script_supports_body_injection_with_post_default()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string script = tools.build_attack_script("https://target.test/items", "xss", injection: "body");

        StringAssert.Contains(script, "method = POST");
        StringAssert.Contains(script, "request.body = json.Stringify(new { value = payload })");
    }

    [TestMethod]
    public void Build_attack_script_rejects_unknown_category()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string result = tools.build_attack_script("https://target.test", "not-a-category");

        StringAssert.Contains(result, "No payloads");
    }

    [TestMethod]
    public void List_payload_categories_includes_new_categories()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string json = tools.list_payload_categories();

        StringAssert.Contains(json, "ldap");
        StringAssert.Contains(json, "header_injection");
        StringAssert.Contains(json, "prototype_pollution");
    }

    [TestMethod]
    public void Analyze_responses_flags_status_and_timing_anomalies()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string json = tools.analyze_responses(
            baseline_status: 200, baseline_size_bytes: 1000, baseline_duration_ms: 40,
            candidate_status: 500, candidate_size_bytes: 1000, candidate_duration_ms: 5040);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.IsTrue(parsed.RootElement.GetProperty("isAnomalous").GetBoolean());
        string anomalies = parsed.RootElement.GetProperty("anomalies").GetRawText();
        StringAssert.Contains(anomalies, "status changed");
        StringAssert.Contains(anomalies, "timing anomaly");
    }

    [TestMethod]
    public void Analyze_responses_reports_no_anomaly_for_identical_responses()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string json = tools.analyze_responses(200, 1000, 50, 200, 1000, 55);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.IsFalse(parsed.RootElement.GetProperty("isAnomalous").GetBoolean());
        Assert.AreEqual(0, parsed.RootElement.GetProperty("anomalies").GetArrayLength());
    }

    [TestMethod]
    public void Build_fuzz_script_emits_runnable_fuzz_harness_with_scope_guard()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string script = tools.build_fuzz_script("https://target.test/search", "sqli");

        StringAssert.Contains(script, "request {");
        StringAssert.Contains(script, "flow {");
        StringAssert.Contains(script, "fuzz.AllowHost(\"target.test\")");
        StringAssert.Contains(script, "await fuzz.Run(payloads.Category(\"sqli\")");
        StringAssert.Contains(script, "new ForRest.Scripting.FuzzOptions");
        StringAssert.Contains(script, "result.Summarize()");
        StringAssert.Contains(script, "foreach f in result.Findings");
        StringAssert.Contains(script, "stash.Commit()");
        StringAssert.Contains(script, "?q=");
    }

    [TestMethod]
    public void Build_fuzz_script_supports_body_injection_and_omits_scope_when_unrestricted()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string script = tools.build_fuzz_script(
            "https://target.test/items", "xss", injection: "body", restrict_to_host: false);

        StringAssert.Contains(script, "method = POST");
        StringAssert.Contains(script, "request.body = json.Stringify(new { value = payload })");
        Assert.IsFalse(script.Contains("fuzz.AllowHost", System.StringComparison.Ordinal));
    }

    [TestMethod]
    public void Build_fuzz_script_rejects_unknown_category()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string result = tools.build_fuzz_script("https://target.test", "not-a-category");

        StringAssert.Contains(result, "No payloads");
    }

    #endregion

    #region Host-less degradation

    [TestMethod]
    public async Task Workbench_tools_report_missing_host_without_a_bridge()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        StringAssert.Contains(await tools.list_workspaces(), "No desktop canvas host");
        StringAssert.Contains(await tools.get_active_request(), "No desktop canvas host");
        StringAssert.Contains(await tools.create_workspace("Demo"), "No desktop canvas host");
        StringAssert.Contains(await tools.execute_script("ws", "name \"x\""), "No desktop canvas host");
    }

    #endregion

    #region Active document & workspace listing

    [TestMethod]
    public async Task Get_active_request_returns_json_and_redacts_secrets()
    {
        FakeHost host = new()
        {
            ActiveDocument = new ForRestMcpDocumentSnapshot(
                DocumentId: "/requests/demo",
                Title: "Demo",
                Language: "forrest",
                SourceText: "secret token = \"super-secret\"\nname \"Demo\"\n",
                WorkspaceName: "Demo Workspace"),
        };
        ForRestMcpTools tools = CreateTools(host);

        string json = await tools.get_active_request();

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.AreEqual("/requests/demo", parsed.RootElement.GetProperty("id").GetString());
        Assert.AreEqual("Demo Workspace", parsed.RootElement.GetProperty("workspace").GetString());
        string source = parsed.RootElement.GetProperty("source").GetString()!;
        StringAssert.Contains(source, "secret token = \"***\"");
        Assert.IsFalse(source.Contains("super-secret", System.StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Replace_active_request_rejects_empty_payload()
    {
        ForRestMcpTools tools = CreateTools(new FakeHost());

        string result = await tools.replace_active_request("");

        StringAssert.Contains(result, "must not be empty");
    }

    [TestMethod]
    public async Task Replace_active_request_forwards_to_host()
    {
        FakeHost host = new();
        ForRestMcpTools tools = CreateTools(host);

        string result = await tools.replace_active_request("name \"New\"");

        StringAssert.Contains(result, "replaced");
        Assert.AreEqual("name \"New\"", host.LastReplacement);
    }

    [TestMethod]
    public async Task List_workspaces_json_describes_host_workspaces()
    {
        FakeHost host = new()
        {
            Workspaces =
            [
                new ForRestMcpWorkspaceSummary(
                    Id: "ws-1",
                    Name: "Demo",
                    Scripts: [new ForRestMcpScriptSummary("Get Users", "GET", "https://example.test/users", "/requests/get-users")]),
            ],
        };
        ForRestMcpTools tools = CreateTools(host);

        string json = await tools.list_workspaces();

        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement workspace = parsed.RootElement[0];
        Assert.AreEqual("ws-1", workspace.GetProperty("id").GetString());
        Assert.AreEqual("Demo", workspace.GetProperty("name").GetString());
        JsonElement script = workspace.GetProperty("scripts")[0];
        Assert.AreEqual("Get Users", script.GetProperty("name").GetString());
        Assert.AreEqual("/requests/get-users", script.GetProperty("location").GetString());
    }

    #endregion

    #region CRUD passthrough

    [TestMethod]
    public async Task Create_workspace_reports_new_id()
    {
        FakeHost host = new();
        ForRestMcpTools tools = CreateTools(host);

        string json = await tools.create_workspace("Fuzzing");

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.IsTrue(parsed.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.AreEqual("new-id", parsed.RootElement.GetProperty("id").GetString());
        Assert.AreEqual("Fuzzing", host.LastCreatedWorkspaceName);
    }

    [TestMethod]
    public async Task Create_script_rejects_empty_source_before_touching_host()
    {
        FakeHost host = new();
        ForRestMcpTools tools = CreateTools(host);

        string result = await tools.create_script("ws", "Probe", "");

        StringAssert.Contains(result, "must not be empty");
        Assert.IsNull(host.LastCreatedScriptName);
    }

    #endregion

    #region Execution & results

    [TestMethod]
    public async Task Execute_script_maps_response_tests_logs_and_stash()
    {
        FakeHost host = new()
        {
            Execution = new ForRestMcpExecutionResult(
                Succeeded: true,
                State: "Completed",
                Diagnostics: [],
                RunId: "run-1",
                Response: new ForRestMcpResponseView(
                    Status: 200,
                    ReasonPhrase: "OK",
                    ContentType: "application/json",
                    SizeBytes: 12,
                    DurationMilliseconds: 42,
                    Headers: [new ForRestMcpHeaderView("Content-Type", "application/json")],
                    Cookies: [],
                    Body: "{\"ok\":true}",
                    RawResponse: "HTTP/1.1 200 OK",
                    Label: null),
                Tests: [new ForRestMcpTestView("status is ok", "Passed", "")],
                Logs: [new ForRestMcpLogView("Info", "done", System.DateTimeOffset.UtcNow)],
                Stash: new ForRestMcpStashView(["Status"], [new Dictionary<string, string> { ["Status"] = "200" }]),
                ErrorMessage: null),
        };
        ForRestMcpTools tools = CreateTools(host);

        string json = await tools.execute_script("ws", "name \"x\"\nmethod GET\nurl \"https://x.test\"");

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.IsTrue(parsed.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.AreEqual("run-1", parsed.RootElement.GetProperty("runId").GetString());
        Assert.AreEqual(200, parsed.RootElement.GetProperty("response").GetProperty("status").GetInt32());
        Assert.AreEqual(1, parsed.RootElement.GetProperty("tests").GetArrayLength());
        Assert.AreEqual("Status", parsed.RootElement.GetProperty("stash").GetProperty("columns")[0].GetString());
    }

    [TestMethod]
    public async Task Grep_run_response_finds_matching_lines()
    {
        FakeHost host = new()
        {
            Run = SampleRun("alpha\nBETA token=abc\ngamma"),
        };
        ForRestMcpTools tools = CreateTools(host);

        string json = await tools.grep_run_response("ws", "run-1", "token", ignore_case: true);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.AreEqual(1, parsed.RootElement.GetProperty("totalMatches").GetInt32());
        JsonElement match = parsed.RootElement.GetProperty("matches")[0];
        Assert.AreEqual(2, match.GetProperty("line").GetInt32());
        StringAssert.Contains(match.GetProperty("text").GetString(), "token=abc");
    }

    [TestMethod]
    public async Task Grep_run_response_reports_invalid_regex()
    {
        FakeHost host = new() { Run = SampleRun("anything") };
        ForRestMcpTools tools = CreateTools(host);

        string result = await tools.grep_run_response("ws", "run-1", "[", is_regex: true);

        StringAssert.Contains(result, "Invalid regular expression");
    }

    [TestMethod]
    public async Task Get_run_response_truncates_body_when_requested()
    {
        FakeHost host = new() { Run = SampleRun("0123456789abcdef") };
        ForRestMcpTools tools = CreateTools(host);

        string json = await tools.get_run_response("ws", "run-1", max_body_chars: 4);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.AreEqual("0123", parsed.RootElement.GetProperty("body").GetString());
        Assert.IsTrue(parsed.RootElement.GetProperty("bodyTruncated").GetBoolean());
    }

    [TestMethod]
    public async Task Get_run_logs_returns_tests_and_console_entries()
    {
        FakeHost host = new() { Run = SampleRun("body") };
        ForRestMcpTools tools = CreateTools(host);

        string json = await tools.get_run_logs("ws", "run-1");

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.AreEqual("Completed", parsed.RootElement.GetProperty("state").GetString());
        Assert.AreEqual(1, parsed.RootElement.GetProperty("logs").GetArrayLength());
    }

    [TestMethod]
    public async Task Get_run_redacts_credential_headers_in_raw_request()
    {
        ForRestMcpRunDetail run = SampleRun("body") with
        {
            RawRequest = "POST https://x.test/v1\nAuthorization: Bearer abc123\nX-Api-Key: key-456\nAccept: application/json",
        };
        FakeHost host = new() { Run = run };
        ForRestMcpTools tools = CreateTools(host);

        string json = await tools.get_run("ws", "run-1");

        StringAssert.Contains(json, "Authorization: ***");
        StringAssert.Contains(json, "Accept: application/json");
        Assert.IsFalse(json.Contains("abc123", System.StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("key-456", System.StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Get_run_reports_missing_run()
    {
        ForRestMcpTools tools = CreateTools(new FakeHost());

        string result = await tools.get_run("ws", "missing");

        StringAssert.Contains(result, "No run found");
    }

    #endregion

    #region Settings

    [TestMethod]
    public void Get_server_settings_never_returns_the_token()
    {
        FakeHost host = new()
        {
            Settings = new ForRestMcpServerSettingsView(true, "127.0.0.1", 7341, HasAuthToken: true, 4, "http://127.0.0.1:7341/"),
        };
        ForRestMcpTools tools = CreateTools(host);

        string json = tools.get_server_settings();

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.IsTrue(parsed.RootElement.GetProperty("hasAuthToken").GetBoolean());
        // The boolean flag is exposed, but the raw token value/field never is.
        List<string> propertyNames = [.. parsed.RootElement.EnumerateObject().Select(static property => property.Name)];
        CollectionAssert.DoesNotContain(propertyNames, "authToken");
        CollectionAssert.DoesNotContain(propertyNames, "token");
    }

    [TestMethod]
    public void Update_settings_text_rejects_empty_input()
    {
        ForRestMcpTools tools = CreateTools(new FakeHost());

        string result = tools.update_settings_text("   ");

        StringAssert.Contains(result, "must not be empty");
    }

    #endregion

    #region Settings secret redaction

    [TestMethod]
    public void Settings_redactor_hides_known_secret_values()
    {
        string toml =
            "license = \"LIC-123\"\n" +
            "[ai]\n" +
            "enabled = true\n" +
            "provider = \"openai\"\n" +
            "api = \"openai\"\n" +
            "api_key = \"sk-super-secret\"\n" +
            "custom_headers = \"X-Api-Key: shh\"\n" +
            "[mcp]\n" +
            "port = 7341\n" +
            "auth_token = \"tok-secret\"\n";

        string redacted = McpSettingsRedactor.Redact(toml);

        Assert.IsFalse(redacted.Contains("sk-super-secret", System.StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("tok-secret", System.StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("LIC-123", System.StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("X-Api-Key: shh", System.StringComparison.Ordinal));
        // Non-secret values are preserved, including the provider family 'api'.
        StringAssert.Contains(redacted, "provider = \"openai\"");
        StringAssert.Contains(redacted, "api = \"openai\"");
        StringAssert.Contains(redacted, "port = 7341");
    }

    [TestMethod]
    public void Settings_restore_swaps_marker_back_but_keeps_intentional_changes()
    {
        string original =
            "[ai]\n" +
            "api_key = \"sk-original\"\n" +
            "[mcp]\n" +
            "auth_token = \"tok-original\"\n";
        string edited =
            "[ai]\n" +
            "api_key = \"***\"\n" +          // untouched -> restore original
            "[mcp]\n" +
            "auth_token = \"tok-new\"\n";    // deliberately changed -> keep

        string restored = McpSettingsRedactor.Restore(original, edited);

        StringAssert.Contains(restored, "api_key = \"sk-original\"");
        StringAssert.Contains(restored, "auth_token = \"tok-new\"");
    }

    #endregion

    #region Browser automation

    [TestMethod]
    public async Task Browser_navigate_and_click_pass_through_to_host()
    {
        FakeHost host = new();
        ForRestMcpTools tools = CreateTools(host);

        string navigated = await tools.browser_navigate("https://app.test/login");
        string clicked = await tools.browser_click("role=button:Sign in");

        StringAssert.Contains(navigated, "https://app.test/login");
        StringAssert.Contains(clicked, "role=button:Sign in");
        CollectionAssert.Contains(host.BrowserCalls, "navigate:https://app.test/login");
        CollectionAssert.Contains(host.BrowserCalls, "click:role=button:Sign in");
    }

    [TestMethod]
    public async Task Browser_query_returns_stable_selectors()
    {
        ForRestMcpTools tools = CreateTools(new FakeHost());

        string json = await tools.browser_query("#go");

        StringAssert.Contains(json, "#go");
        StringAssert.Contains(json, "Xpath");
    }

    [TestMethod]
    public async Task Browser_snapshot_lists_elements()
    {
        ForRestMcpTools tools = CreateTools(new FakeHost());

        string json = await tools.browser_snapshot();

        StringAssert.Contains(json, "Elements");
        StringAssert.Contains(json, "https://x.test/");
    }

    [TestMethod]
    public async Task Show_in_app_passes_target_and_id_to_host()
    {
        FakeHost host = new();
        ForRestMcpTools tools = CreateTools(host);

        string result = await tools.show_in_app("browser");

        StringAssert.Contains(result, "Showing browser");
        CollectionAssert.Contains(host.BrowserCalls, "show:browser:");
    }

    [TestMethod]
    public async Task Browser_tools_report_missing_host()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string result = await tools.browser_navigate("https://x.test");

        StringAssert.Contains(result, "desktop");
    }

    #endregion

    #region Helpers

    private static ForRestMcpTools CreateTools(IForRestMcpHost? host)
    {
        ForRestAiKnowledgeCatalog catalog = new();
        AiDocumentationSearchService search = new(catalog.GetDocuments());
        return new ForRestMcpTools(catalog, search, () => host);
    }

    private static ForRestMcpRunDetail SampleRun(string body)
    {
        ForRestMcpResponseView response = new(
            Status: 200,
            ReasonPhrase: "OK",
            ContentType: "text/plain",
            SizeBytes: body.Length,
            DurationMilliseconds: 5,
            Headers: [],
            Cookies: [],
            Body: body,
            RawResponse: "HTTP/1.1 200 OK\n\n" + body,
            Label: null);

        ForRestMcpRunSummary summary = new(
            Id: "run-1",
            RequestName: "Sample",
            State: "Completed",
            Iteration: 1,
            StartedUtc: System.DateTimeOffset.UtcNow,
            CompletedUtc: System.DateTimeOffset.UtcNow,
            TargetUri: "https://x.test",
            Status: 200,
            DurationMilliseconds: 5,
            ErrorMessage: "");

        return new ForRestMcpRunDetail(
            Summary: summary,
            Response: response,
            Responses: [response],
            Tests: [new ForRestMcpTestView("ok", "Passed", "")],
            Logs: [new ForRestMcpLogView("Info", "log line", System.DateTimeOffset.UtcNow)],
            Stash: null,
            RawRequest: "GET https://x.test");
    }

    private sealed class FakeHost : IForRestMcpHost
    {
        public ForRestMcpDocumentSnapshot? ActiveDocument { get; set; }

        public List<ForRestMcpWorkspaceSummary> Workspaces { get; set; } = [];

        public ForRestMcpExecutionResult? Execution { get; set; }

        public ForRestMcpRunDetail? Run { get; set; }

        public ForRestMcpServerSettingsView Settings { get; set; } =
            new(false, "127.0.0.1", 7341, false, 4, "http://127.0.0.1:7341/");

        public string? LastReplacement { get; private set; }

        public string? LastCreatedWorkspaceName { get; private set; }

        public string? LastCreatedScriptName { get; private set; }

        public Task<ForRestMcpDocumentSnapshot?> GetActiveDocument(CancellationToken cancellationToken)
            => Task.FromResult(ActiveDocument);

        public Task<ForRestMcpUpdateResult> ReplaceActiveDocument(string newSource, CancellationToken cancellationToken)
        {
            LastReplacement = newSource;
            return Task.FromResult(ForRestMcpUpdateResult.Success());
        }

        public Task<ForRestMcpMutationResult> SetActiveDocument(string workspaceId, string location, CancellationToken cancellationToken)
            => Task.FromResult(ForRestMcpMutationResult.Ok("selected", location));

        public Task<IReadOnlyList<ForRestMcpWorkspaceSummary>> ListWorkspaces(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ForRestMcpWorkspaceSummary>>(Workspaces);

        public Task<ForRestMcpWorkspaceSummary?> GetWorkspace(string workspaceId, CancellationToken cancellationToken)
            => Task.FromResult(Workspaces.FirstOrDefault(workspace => workspace.Id == workspaceId));

        public Task<ForRestMcpMutationResult> CreateWorkspace(string name, CancellationToken cancellationToken)
        {
            LastCreatedWorkspaceName = name;
            return Task.FromResult(ForRestMcpMutationResult.Ok("created", "new-id"));
        }

        public Task<ForRestMcpMutationResult> RenameWorkspace(string workspaceId, string newName, CancellationToken cancellationToken)
            => Task.FromResult(ForRestMcpMutationResult.Ok("renamed", workspaceId));

        public Task<ForRestMcpMutationResult> DeleteWorkspace(string workspaceId, CancellationToken cancellationToken)
            => Task.FromResult(ForRestMcpMutationResult.Ok("deleted"));

        public Task<ForRestMcpScriptDetail?> GetScript(string workspaceId, string location, CancellationToken cancellationToken)
            => Task.FromResult<ForRestMcpScriptDetail?>(null);

        public Task<ForRestMcpMutationResult> CreateScript(string workspaceId, string name, string source, CancellationToken cancellationToken)
        {
            LastCreatedScriptName = name;
            return Task.FromResult(ForRestMcpMutationResult.Ok("created", "/requests/probe"));
        }

        public Task<ForRestMcpMutationResult> UpdateScript(string workspaceId, string location, string newSource, CancellationToken cancellationToken)
            => Task.FromResult(ForRestMcpMutationResult.Ok("updated", location));

        public Task<ForRestMcpMutationResult> RenameScript(string workspaceId, string location, string newName, CancellationToken cancellationToken)
            => Task.FromResult(ForRestMcpMutationResult.Ok("renamed", location));

        public Task<ForRestMcpMutationResult> DeleteScript(string workspaceId, string location, CancellationToken cancellationToken)
            => Task.FromResult(ForRestMcpMutationResult.Ok("deleted"));

        public ForRestMcpCompileResult CompileScript(string workspaceId, string source)
            => new(true, [], "GET", "https://x.test", "Sample");

        public Task<ForRestMcpExecutionResult> ExecuteScript(string workspaceId, string source, string? requestName, CancellationToken cancellationToken)
            => Task.FromResult(Execution ?? throw new System.InvalidOperationException("No execution configured."));

        public Task<ForRestMcpExecutionResult> ExecuteStoredScript(string workspaceId, string location, CancellationToken cancellationToken)
            => Task.FromResult(Execution ?? throw new System.InvalidOperationException("No execution configured."));

        public Task<IReadOnlyList<ForRestMcpRunSummary>> ListRuns(string workspaceId, int max, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ForRestMcpRunSummary>>(Run is null ? [] : [Run.Summary]);

        public Task<ForRestMcpRunDetail?> GetRun(string workspaceId, string runId, CancellationToken cancellationToken)
            => Task.FromResult(Run is not null && Run.Summary.Id == runId ? Run : null);

        public ForRestMcpServerSettingsView GetServerSettings() => Settings;

        public string GetSettingsText() => "[mcp]\nenabled = true\n";

        public ForRestMcpMutationResult UpdateSettingsText(string rawToml) => ForRestMcpMutationResult.Ok("saved");

        public List<string> BrowserCalls { get; } = [];

        public bool BrowserAvailable => true;

        public Task<string> BrowserNavigate(string url, CancellationToken cancellationToken)
        {
            BrowserCalls.Add($"navigate:{url}");
            return Task.FromResult($"Navigated to {url}.");
        }

        public Task<ForRestMcpBrowserSnapshotView> BrowserSnapshot(CancellationToken cancellationToken)
            => Task.FromResult(new ForRestMcpBrowserSnapshotView(true, "https://x.test/", "X",
                [new ForRestMcpBrowserElementView(true, "button", "go", "Go", "#go", "/html/body/button[1]", "button", "Go", 1, 2, 3, 4)]));

        public Task<string> BrowserScreenshot(CancellationToken cancellationToken) => Task.FromResult("QUJD");

        public Task<ForRestMcpBrowserElementView> BrowserQuery(string target, CancellationToken cancellationToken)
        {
            BrowserCalls.Add($"query:{target}");
            return Task.FromResult(new ForRestMcpBrowserElementView(true, "button", "go", "Go", "#go", "/html/body/button[1]", "button", "Go", 1, 2, 3, 4));
        }

        public Task<string> BrowserClick(string target, CancellationToken cancellationToken)
        {
            BrowserCalls.Add($"click:{target}");
            return Task.FromResult($"Clicked {target}.");
        }

        public Task<string> BrowserType(string target, string text, CancellationToken cancellationToken)
        {
            BrowserCalls.Add($"type:{target}={text}");
            return Task.FromResult($"Typed into {target}.");
        }

        public Task<string> BrowserPress(string keys, CancellationToken cancellationToken)
        {
            BrowserCalls.Add($"press:{keys}");
            return Task.FromResult($"Pressed {keys}.");
        }

        public Task<ForRestMcpBrowserElementView> BrowserWaitFor(string target, int timeoutMs, CancellationToken cancellationToken)
            => Task.FromResult(new ForRestMcpBrowserElementView(true, "div", "ready", "Ready", "#ready", "/html/body/div[1]", "", "", 0, 0, 0, 0));

        public Task<string> BrowserEvaluate(string expression, CancellationToken cancellationToken) => Task.FromResult("null");

        public Task<ForRestMcpMutationResult> ShowInApp(string target, string? id, CancellationToken cancellationToken)
        {
            BrowserCalls.Add($"show:{target}:{id}");
            return Task.FromResult(ForRestMcpMutationResult.Ok($"Showing {target}."));
        }
    }

    #endregion
}
