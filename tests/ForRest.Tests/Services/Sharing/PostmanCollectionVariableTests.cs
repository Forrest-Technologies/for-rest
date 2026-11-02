using System.Text.Json;
using ForRest.Mcp;
using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class PostmanCollectionVariableTests
{
    #region Public Methods

    [TestMethod]
    public void Imported_requests_resolve_collection_variables_without_running_a_separate_document()
    {
        const string collection = """
            {
              "info":{"name":"Example","_postman_id":"fixture"},
              "variable":[
                {"key":"baseUrl","value":"https://fixture.test"},
                {"key":"version","value":2},
                {"key":"enabled","value":true},
                {"key":"label","value":"first\tsecond"},
                {"key":"unused","value":"disabled-value","disabled":true}
              ],
              "item":[
                {"name":"First","request":{"method":"GET","url":"{{baseUrl}}/v{{version}}?enabled={{enabled}}",
                  "header":[{"key":"X-Label","value":"{{label}}"}]}},
                {"name":"Folder","item":[
                  {"name":"Second","request":{"method":"GET","url":"{{baseUrl}}/nested"}}
                ]}
              ]
            }
            """;
        var documents = new PostmanImportService().Convert(collection, out _);
        var requests = documents.Where(document => document.Name != "Collection variables").ToArray();
        Assert.HasCount(2, requests);
        foreach (var document in requests)
        {
            var compilation = new ForRestScriptCompiler(new()).Compile(document.Source, new());
            Assert.IsTrue(compilation.Succeeded, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
            var payload = compilation.Payload!;

            var prepared = new RequestCompiler(new VariableResolver()).Prepare(payload.Request, [], [], [], [], []);

            Assert.IsTrue(prepared.Succeeded, prepared.ErrorMessage);
            Assert.AreEqual("fixture.test", prepared.Value!.Uri.Host);
            Assert.DoesNotContain("{{", prepared.Value.Uri.AbsoluteUri);
            Assert.IsFalse(payload.Request.Variables.Any(variable => variable.Key == "unused"));
            if (document.Name == "First")
            {
                Assert.AreEqual("/v2", prepared.Value.Uri.AbsolutePath);
                Assert.AreEqual("?enabled=true", prepared.Value.Uri.Query);
                Assert.AreEqual("first\tsecond", prepared.Value.Headers.Single(header => header.Key == "X-Label").Value);
            }
        }
    }

    [TestMethod]
    public void Sensitive_collection_values_are_redacted_in_both_reference_and_request_copies()
    {
        const string collection = """
            {
              "info":{"_postman_id":"fixture"},
              "variable":[
                {"key":"apiToken","value":"fake-postman-token-1873"},
                {"key":"privateValue","value":"fake-private-value-4321","type":"secret"}
              ],
              "item":[{"name":"R","request":{
                "url":"https://fixture.test/",
                "auth":{"type":"bearer","bearer":[{"key":"token","value":"{{apiToken}}"}]},
                "header":[{"key":"X-Private","value":"{{privateValue}}"}]
              }}]
            }
            """;
        var documents = new PostmanImportService().Convert(collection, out _);
        foreach (var document in documents)
        {
            var redacted = McpSecretRedactor.Redact(document.Source);
            Assert.DoesNotContain("fake-postman-token-1873", redacted);
            Assert.DoesNotContain("fake-private-value-4321", redacted);
        }

        var compilation = new ForRestScriptCompiler(new()).Compile(documents.Single(document => document.Name == "R").Source, new());
        Assert.IsTrue(compilation.Succeeded);
        var payload = compilation.Payload!;
        var runtime = new ForRestRuntimeVariableSeedEvaluator().Evaluate(payload.RuntimeSeeds);
        var prepared = new RequestCompiler(new VariableResolver()).Prepare(payload.Request, [], [], [], [], runtime);

        Assert.IsTrue(prepared.Succeeded);
        Assert.HasCount(2, runtime.Where(variable => variable.IsSecret).ToArray());
        Assert.AreEqual("Bearer fake-postman-token-1873", prepared.Value!.Headers.Single(header => header.Key == "Authorization").Value);
        Assert.AreEqual("fake-private-value-4321", prepared.Value.Headers.Single(header => header.Key == "X-Private").Value);
    }

    [TestMethod]
    [DataRow("clé")]
    [DataRow("ключ")]
    [DataRow("令牌")]
    [DataRow("api-token")]
    [DataRow("token.v2")]
    [DataRow("2token")]
    public void Unsupported_collection_variable_names_do_not_invalidate_unrelated_requests(string key)
    {
        var collection = JsonSerializer.Serialize(new
        {
            info = new { _postman_id = "fixture" },
            variable = new[] { new { key, value = "unused-value" } },
            item = new[] { new { name = "R", request = new { url = "https://fixture.test/" } } },
        });

        var documents = new PostmanImportService().Convert(collection, out _);

        Assert.HasCount(1, documents);
        var compilation = new ForRestScriptCompiler(new()).Compile(documents[0].Source, new());
        Assert.IsTrue(compilation.Succeeded, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
    }

    [TestMethod]
    public void Reference_document_remains_available_for_collections_without_requests()
    {
        const string collection = """
            {"info":{"_postman_id":"fixture"},"variable":[{"key":"host","value":"https://fixture.test"}],"item":[]}
            """;

        var documents = new PostmanImportService().Convert(collection, out _);

        Assert.HasCount(1, documents);
        Assert.AreEqual("Collection variables", documents[0].Name);
        FrsParseAssert.Parses(documents[0].Source);
    }

    #endregion
}
