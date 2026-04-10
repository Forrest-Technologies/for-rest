using System.Collections.Generic;
using System.Text.Json;
using ForRest.Mcp;
using ForRest.Services.AI;

namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class ForRestMcpToolsTests
{
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
    public void Get_active_request_reports_missing_host_when_no_bridge_registered()
    {
        ForRestMcpTools tools = CreateTools(host: null);

        string result = tools.get_active_request();

        StringAssert.Contains(result, "No desktop canvas host");
    }

    [TestMethod]
    public void Get_active_request_returns_json_when_host_supplies_snapshot()
    {
        ForRestMcpDocumentSnapshot snapshot = new(
            DocumentId: "/requests/demo",
            Title: "Demo",
            Language: "forrest",
            SourceText: "name \"Demo\"\nmethod GET\nurl \"https://example.test\"\n",
            WorkspaceName: "Demo Workspace");

        ForRestMcpTools tools = CreateTools(host: new FakeHost(snapshot));

        string json = tools.get_active_request();

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.AreEqual("/requests/demo", parsed.RootElement.GetProperty("id").GetString());
        Assert.AreEqual("Demo", parsed.RootElement.GetProperty("title").GetString());
        Assert.AreEqual("Demo Workspace", parsed.RootElement.GetProperty("workspace").GetString());
    }

    [TestMethod]
    public void Replace_active_request_rejects_empty_payload()
    {
        ForRestMcpTools tools = CreateTools(host: new FakeHost());

        string result = tools.replace_active_request("");

        StringAssert.Contains(result, "must not be empty");
    }

    [TestMethod]
    public void Replace_active_request_applies_host_update_on_success()
    {
        FakeHost host = new();
        ForRestMcpTools tools = CreateTools(host);

        string result = tools.replace_active_request("name \"New\"");

        StringAssert.Contains(result, "replaced");
        Assert.AreEqual("name \"New\"", host.LastReplacement);
    }

    [TestMethod]
    public void List_workspaces_json_describes_host_workspaces()
    {
        FakeHost host = new()
        {
            Workspaces =
            [
                new ForRestMcpWorkspaceSummary(
                    Id: "ws-1",
                    Name: "Demo",
                    Scripts: new List<ForRestMcpScriptSummary>
                    {
                        new("Get Users", "GET", "https://example.test/users"),
                    }),
            ],
        };

        ForRestMcpTools tools = CreateTools(host);

        string json = tools.list_workspaces();

        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement workspace = parsed.RootElement[0];
        Assert.AreEqual("ws-1", workspace.GetProperty("id").GetString());
        Assert.AreEqual("Demo", workspace.GetProperty("name").GetString());
        Assert.AreEqual("Get Users", workspace.GetProperty("scripts")[0].GetProperty("name").GetString());
    }

    private static ForRestMcpTools CreateTools(IForRestMcpHost? host)
    {
        ForRestAiKnowledgeCatalog catalog = new();
        AiDocumentationSearchService search = new(catalog.GetDocuments());
        return new ForRestMcpTools(catalog, search, () => host);
    }

    private sealed class FakeHost : IForRestMcpHost
    {
        public ForRestMcpDocumentSnapshot? Snapshot { get; set; }

        public List<ForRestMcpWorkspaceSummary> Workspaces { get; set; } = [];

        public string? LastReplacement { get; set; }

        public FakeHost()
        {
        }

        public FakeHost(ForRestMcpDocumentSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public ForRestMcpDocumentSnapshot? GetActiveDocument() => Snapshot;

        public ForRestMcpUpdateResult ReplaceActiveDocument(string newSource)
        {
            LastReplacement = newSource;
            return ForRestMcpUpdateResult.Success();
        }

        public IReadOnlyList<ForRestMcpWorkspaceSummary> ListWorkspaces() => Workspaces;
    }
}
