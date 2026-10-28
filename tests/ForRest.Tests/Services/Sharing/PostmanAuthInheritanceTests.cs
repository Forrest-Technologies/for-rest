using ForRest.Mcp;
using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class PostmanAuthInheritanceTests
{
    #region Private Fields

    private const string Collection = """
        {
          "info":{"name":"Fixture","_postman_id":"fixture"},
          "auth":{"type":"bearer","bearer":{"token":"fake-root-token-4257"}},
          "item":[
            {"name":"Root","request":{"url":"https://fixture.test/"}},
            {"name":"Null","request":{"url":"https://fixture.test/","auth":null}},
            {"name":"Public","request":{"url":"https://fixture.test/","auth":{"type":"noauth"}}},
            {"name":"Folder","auth":{"type":"basic","basic":{"username":"reader","password":"fake-folder-password-4257"}},
              "item":[
                {"name":"Child","request":{"url":"https://fixture.test/","auth":null}},
                {"name":"Nested","auth":null,"item":[
                  {"name":"Child","request":{"url":"https://fixture.test/"}},
                  {"name":"Override","request":{"url":"https://fixture.test/",
                    "auth":{"type":"bearer","bearer":[{"key":"token","value":"fake-request-token-4257"}]}}}
                ]},
                {"name":"Public","auth":{"type":"noauth"},"item":[
                  {"name":"Child","request":{"url":"https://fixture.test/"}},
                  {"name":"Override","request":{"url":"https://fixture.test/",
                    "auth":{"type":"bearer","bearer":{"token":"fake-request-token-4257"}}}}
                ]}
              ]},
            {"name":"After folder","request":{"url":"https://fixture.test/"}},
            {"name":"Manual","request":{"url":"https://fixture.test/","auth":{"type":"noauth"},
              "header":[{"key":"Authorization","value":"manual-fixture-value"}]}}
          ]
        }
        """;

    #endregion

    #region Public Methods

    [TestMethod]
    [DataRow("Root", "Bearer fake-root-token-4257")]
    [DataRow("Null", "Bearer fake-root-token-4257")]
    [DataRow("Folder / Child", "Basic cmVhZGVyOmZha2UtZm9sZGVyLXBhc3N3b3JkLTQyNTc=")]
    [DataRow("Folder / Nested / Child", "Basic cmVhZGVyOmZha2UtZm9sZGVyLXBhc3N3b3JkLTQyNTc=")]
    [DataRow("Folder / Nested / Override", "Bearer fake-request-token-4257")]
    [DataRow("Folder / Public / Override", "Bearer fake-request-token-4257")]
    [DataRow("After folder", "Bearer fake-root-token-4257")]
    [DataRow("Manual", "manual-fixture-value")]
    public void Requests_use_the_nearest_authentication_override(string name, string expectedHeader)
    {
        var source = new PostmanImportService().Convert(Collection, out _).Single(item => item.Name == name).Source;

        var prepared = Prepare(source);

        Assert.AreEqual(expectedHeader, prepared.Headers.Single(item => item.Key == "Authorization").Value);
        foreach (var credential in new[] { "fake-root-token-4257", "fake-folder-password-4257", "fake-request-token-4257" })
        {
            Assert.DoesNotContain(credential, McpSecretRedactor.Redact(source));
            Assert.DoesNotContain(credential, FrsSecretRedactor.Redact(source));
        }
    }

    [TestMethod]
    [DataRow("Public")]
    [DataRow("Folder / Public / Child")]
    public void Explicit_no_authentication_disables_inherited_credentials(string name)
    {
        var source = new PostmanImportService().Convert(Collection, out _).Single(item => item.Name == name).Source;

        var prepared = Prepare(source);

        Assert.AreEqual(AuthMode.None, prepared.Auth.Mode);
        Assert.IsFalse(prepared.Headers.Any(item => item.Key == "Authorization"));
        Assert.DoesNotContain("fake-", source);
    }

    #endregion

    #region Private Methods

    private static PreparedRequest Prepare(string source)
    {
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
        Assert.IsTrue(compilation.Succeeded, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        var payload = compilation.Payload!;
        var runtime = new ForRestRuntimeVariableSeedEvaluator().Evaluate(payload.RuntimeSeeds);
        var prepared = new RequestCompiler(new VariableResolver()).Prepare(payload.Request, [], [], [], [], runtime);
        Assert.IsTrue(prepared.Succeeded, prepared.ErrorMessage);
        return prepared.Value!;
    }

    #endregion
}
