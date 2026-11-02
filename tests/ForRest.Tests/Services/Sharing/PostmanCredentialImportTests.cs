using System.Text.Json;
using ForRest.Mcp;
using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class PostmanCredentialImportTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("bearer", "token", "fake-bearer-5821", "Bearer fake-bearer-5821")]
    [DataRow("basic", "username", "fake-api-user-5821", "Basic ZmFrZS1hcGktdXNlci01ODIxOg==")]
    [DataRow("basic", "password", "fake-password-5821", "Basic OmZha2UtcGFzc3dvcmQtNTgyMQ==")]
    [DataRow("apikey", "value", "fake-api-key-5821", "fake-api-key-5821")]
    public void Literal_authentication_credentials_are_redacted_without_changing_the_header(
        string type, string key, string credential, string expectedHeader)
    {
        foreach (var legacy in new[] { false, true })
        {
            var parameters = new Dictionary<string, string> { [key] = credential };
            if (type == "apikey")
            {
                parameters["key"] = "X-Api-Key";
                parameters["in"] = "header";
            }
            var document = Import(type, parameters, legacy);
            var prepared = Prepare(document.Source);

            Assert.AreEqual(expectedHeader,
                prepared.Headers.Single(item => item.Key == (type == "apikey" ? "X-Api-Key" : "Authorization")).Value);
            Assert.DoesNotContain(credential, McpSecretRedactor.Redact(document.Source));
            Assert.DoesNotContain(credential, FrsSecretRedactor.Redact(document.Source));
        }
    }

    [TestMethod]
    public void Generated_authentication_secrets_do_not_override_collection_variables()
    {
        var document = Import("bearer", new() { ["token"] = "fake-bearer-5821" }, false,
            [new { key = "postman_bearer_token", value = "public-label" }]);

        var prepared = Prepare(document.Source);

        Assert.AreEqual("Bearer fake-bearer-5821", prepared.Headers.Single(item => item.Key == "Authorization").Value);
        Assert.AreEqual("public-label", prepared.Headers.Single(item => item.Key == "X-Label").Value);
        Assert.DoesNotContain("fake-bearer-5821", McpSecretRedactor.Redact(document.Source));
    }

    [TestMethod]
    public void Existing_authentication_templates_keep_their_original_resolution()
    {
        var document = Import("bearer", new() { ["token"] = "prefix-{{credential}}" }, false,
            [new { key = "credential", value = "fake-template-5821" }]);

        var prepared = Prepare(document.Source);

        Assert.AreEqual("Bearer prefix-fake-template-5821",
            prepared.Headers.Single(item => item.Key == "Authorization").Value);
    }

    [TestMethod]
    public void Generated_authentication_secrets_do_not_shadow_external_variable_references()
    {
        var document = Import("bearer", new() { ["token"] = "fake-bearer-5821" }, false);

        var prepared = Prepare(document.Source);

        Assert.AreEqual("{{postman_bearer_token}}", prepared.Headers.Single(item => item.Key == "X-Label").Value);
    }

    [TestMethod]
    public void Query_api_keys_remain_query_parameters_and_are_redacted()
    {
        var document = Import("apikey", new()
        {
            ["key"] = "api_key", ["value"] = "fake-query-key-5821", ["in"] = "query",
        }, false);

        var prepared = Prepare(document.Source);

        Assert.AreEqual("?api_key=fake-query-key-5821", prepared.Uri.Query);
        Assert.DoesNotContain("fake-query-key-5821", McpSecretRedactor.Redact(document.Source));
        Assert.DoesNotContain("fake-query-key-5821", FrsSecretRedactor.Redact(document.Source));
    }

    #endregion

    #region Private Methods

    private static PortableWorkspaceDocument Import(
        string type, Dictionary<string, string> parameters, bool legacy, object[]? variables = null)
    {
        var auth = new Dictionary<string, object>
        {
            ["type"] = type,
            [type] = legacy
                ? parameters
                : parameters.Select(item => new { key = item.Key, value = item.Value }).ToArray(),
        };
        var json = JsonSerializer.Serialize(new
        {
            info = new { name = "Fixture", _postman_id = "fixture" },
            variable = variables ?? [],
            item = new[]
            {
                new
                {
                    name = "Request",
                    request = new
                    {
                        method = "GET", url = "https://fixture.test/", auth,
                        header = new[] { new { key = "X-Label", value = "{{postman_bearer_token}}" } },
                    },
                },
            },
        });
        return new PostmanImportService().Convert(json, out _).Single(item => item.Name == "Request");
    }

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
