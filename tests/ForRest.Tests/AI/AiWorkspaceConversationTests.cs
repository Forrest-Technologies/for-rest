using System.Collections.Generic;
using System.Text.Json;
using ForRest.Services.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AiWorkspaceConversationTests
{
    #region Fakes

    private sealed class FakeWorkspaceHost : IAiWorkspaceHost
    {
        public Dictionary<string, AiWorkspaceScriptDocument> Scripts { get; } = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = [];

        public AiWorkspaceContext? GetWorkspaceContext()
        {
            Calls.Add("ctx");
            List<AiWorkspaceScriptSummary> summaries =
            [
                .. Scripts.Values.Select(static script =>
                    new AiWorkspaceScriptSummary(script.ScriptId, script.Name, "GET", $"https://api/{script.Name}"))
            ];
            return new AiWorkspaceContext("ws1", "Demo WS", summaries);
        }

        public AiWorkspaceScriptDocument? ReadScript(string scriptId)
        {
            Calls.Add($"read:{scriptId}");
            return Scripts.TryGetValue(scriptId, out AiWorkspaceScriptDocument? document) ? document : null;
        }

        public AiActiveDocumentUpdateResult CreateScript(string name, string sourceText)
        {
            Calls.Add($"create:{name}");
            string id = $"/requests/demo/{name.ToLowerInvariant().Replace(' ', '-')}";
            Scripts[id] = new AiWorkspaceScriptDocument(id, name, "forrest", sourceText, []);
            return AiActiveDocumentUpdateResult.Success(id);
        }

        public AiActiveDocumentUpdateResult UpdateScript(string scriptId, string sourceText)
        {
            Calls.Add($"update:{scriptId}");
            if (!Scripts.TryGetValue(scriptId, out AiWorkspaceScriptDocument? existing))
            {
                return AiActiveDocumentUpdateResult.Failure("not found");
            }

            Scripts[scriptId] = existing with { SourceText = sourceText };
            return AiActiveDocumentUpdateResult.Success();
        }
    }

    private sealed class FakeTurnExecutor : IAiTurnExecutor
    {
        public AiTurnExecutionRequest? LastRequest { get; private set; }

        public string Response { get; set; } = "Created the requests.";

        public bool Succeed { get; set; } = true;

        public Task<AiTurnExecutionResult> ExecuteAsync(AiTurnExecutionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new AiTurnExecutionResult(Succeed, Response, [], SessionReset: false, DebugTrace: "trace"));
        }
    }

    #endregion

    #region Helpers

    private static AiSettings EnabledSettings()
    {
        return new AiSettings
        {
            Enabled = true,
            Tools = new AiToolSettings { EnableDocumentPatch = true, MaxPatchCharacters = 8192 },
        };
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    #endregion

    #region Tool Catalog

    [TestMethod]
    public void Catalog_exposes_four_tools_only_when_enabled_with_a_host()
    {
        AiSettings settings = EnabledSettings();
        FakeWorkspaceHost host = new();
        AiWorkspaceToolCatalog catalog = new();

        Assert.AreEqual(4, catalog.GetTools(settings, host).Count);
        Assert.AreEqual(0, catalog.GetTools(settings, null).Count);
        Assert.AreEqual(0, catalog.GetTools(settings with { Enabled = false }, host).Count);
        Assert.AreEqual(0, catalog.GetTools(settings with { Tools = new AiToolSettings { EnableDocumentPatch = false } }, host).Count);
    }

    #endregion

    #region Tool Service

    [TestMethod]
    public void ToolService_create_then_list_round_trips_through_the_host()
    {
        AiSettings settings = EnabledSettings();
        FakeWorkspaceHost host = new();
        AiWorkspaceToolService service = new();

        JsonElement created = Parse(service.CreateScript(settings, host, "List Users", "name \"List Users\"\nmethod GET\nurl \"https://api/users\""));
        Assert.IsTrue(created.GetProperty("succeeded").GetBoolean());
        string scriptId = created.GetProperty("scriptId").GetString()!;
        Assert.IsTrue(host.Scripts.ContainsKey(scriptId));

        JsonElement listed = Parse(service.ListScripts(settings, host));
        Assert.IsTrue(listed.GetProperty("succeeded").GetBoolean());
        Assert.AreEqual(1, listed.GetProperty("scripts").GetArrayLength());
    }

    [TestMethod]
    public void ToolService_read_redacts_secret_values()
    {
        AiSettings settings = EnabledSettings();
        FakeWorkspaceHost host = new();
        AiWorkspaceToolService service = new();
        string id = Parse(service.CreateScript(settings, host, "Auth", "secret token = \"abc123\"\nmethod GET")).GetProperty("scriptId").GetString()!;

        JsonElement read = Parse(service.ReadScript(settings, host, id));
        string source = read.GetProperty("document").GetProperty("sourceText").GetString()!;

        Assert.IsTrue(read.GetProperty("succeeded").GetBoolean());
        Assert.IsTrue(source.Contains("***", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("abc123", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ToolService_read_missing_script_fails()
    {
        AiSettings settings = EnabledSettings();
        FakeWorkspaceHost host = new();
        AiWorkspaceToolService service = new();

        Assert.IsFalse(Parse(service.ReadScript(settings, host, "missing")).GetProperty("succeeded").GetBoolean());
    }

    [TestMethod]
    public void ToolService_update_restores_secret_even_when_agent_saw_only_redacted()
    {
        AiSettings settings = EnabledSettings();
        FakeWorkspaceHost host = new();
        AiWorkspaceToolService service = new();
        string id = Parse(service.CreateScript(settings, host, "Auth", "secret token = \"abc123\"\nmethod GET")).GetProperty("scriptId").GetString()!;

        JsonElement updated = Parse(service.UpdateScript(settings, host, id, "secret token = \"***\"\nmethod POST"));

        Assert.IsTrue(updated.GetProperty("succeeded").GetBoolean());
        Assert.IsTrue(host.Scripts[id].SourceText.Contains("abc123", StringComparison.Ordinal), "the real secret value must survive the rewrite");
        Assert.IsTrue(host.Scripts[id].SourceText.Contains("POST", StringComparison.Ordinal), "the new content must be applied");
    }

    [TestMethod]
    public void ToolService_fails_when_ai_is_disabled()
    {
        FakeWorkspaceHost host = new();
        AiWorkspaceToolService service = new();
        AiSettings disabled = EnabledSettings() with { Enabled = false };

        Assert.IsFalse(Parse(service.ListScripts(disabled, host)).GetProperty("succeeded").GetBoolean());
    }

    #endregion

    #region Conversation Service

    [TestMethod]
    public async Task Conversation_routes_prompt_through_the_workspace_host()
    {
        AiSettings settings = EnabledSettings();
        FakeWorkspaceHost host = new();
        FakeTurnExecutor executor = new();
        AiWorkspaceConversationService service = new(executor);
        string transcript = string.Join("\n", ["## scaffold CRUD for the users API", ""]);

        AiWorkspaceConversationResult result = await service.TryHandleAsync(
            new AiWorkspaceConversationRequest("ws1", "Demo WS", "forrest", transcript, 1, settings, host));

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(executor.LastRequest);
        Assert.AreSame(host, executor.LastRequest!.WorkspaceHost);
        Assert.IsNull(executor.LastRequest!.ActiveDocumentHost);
        Assert.AreEqual("scaffold CRUD for the users API", executor.LastRequest!.Prompt);
        Assert.AreEqual("workspace-assistant:ws1", executor.LastRequest!.ConversationId);
        Assert.AreEqual(1, result.PromptLineNumber);
    }

    [TestMethod]
    public async Task Conversation_renders_response_and_reopens_a_fresh_prompt()
    {
        AiSettings settings = EnabledSettings();
        FakeWorkspaceHost host = new();
        FakeTurnExecutor executor = new() { Response = "Created the requests." };
        AiWorkspaceConversationService service = new(executor);
        string transcript = string.Join("\n", ["## scaffold CRUD for the users API", ""]);

        AiWorkspaceConversationResult result = await service.TryHandleAsync(
            new AiWorkspaceConversationRequest("ws1", "Demo WS", "forrest", transcript, 1, settings, host));

        StringAssert.Contains(result.UpdatedText, "#> Created the requests.");
        Assert.IsTrue(
            result.UpdatedText.Contains("\n## ", StringComparison.Ordinal) || result.UpdatedText.TrimEnd().EndsWith("##", StringComparison.Ordinal),
            "a fresh prompt line should be reopened beneath the rendered response");
    }

    [TestMethod]
    public async Task Conversation_is_not_handled_without_an_actionable_prompt()
    {
        AiSettings settings = EnabledSettings();
        FakeWorkspaceHost host = new();
        AiWorkspaceConversationService service = new(new FakeTurnExecutor());

        AiWorkspaceConversationResult result = await service.TryHandleAsync(
            new AiWorkspaceConversationRequest("ws1", "Demo WS", "forrest", "name \"x\"\nmethod GET", 2, settings, host));

        Assert.IsFalse(result.Handled);
    }

    #endregion
}
