using System.Net;

namespace ForRest.Tests.Domain;

[TestClass]
public sealed class RequestCompilerTests
{
    #region Private Fields

    private readonly RequestCompiler requestCompiler = new(new VariableResolver());

    #endregion

    #region Public Methods

    [TestMethod]
    public void Prepare_renders_url_headers_body_and_bearer_auth()
    {
        var request = new RequestDefinition
        {
            Method = HttpMethodKind.Post,
            UrlTemplate = "https://{{host}}/users/{{userId}}",
            Headers =
            [
                new()
                {
                    Key = "X-Tenant",
                    Value = "{{tenant}}",
                },
            ],
            Auth = new()
            {
                Mode = AuthMode.BearerToken,
                BearerToken = "{{token}}",
            },
            Body = new()
            {
                Mode = RequestBodyMode.Json,
                ContentType = "application/json",
                RawContent = """{"id":"{{userId}}"}""",
            },
            QueryParameters =
            [
                new()
                {
                    Key = "include",
                    Value = "{{tenant}}",
                },
            ],
        };

        var result = requestCompiler.Prepare(
            request,
            [],
            [
                CreateVariable("tenant", "core", VariableScope.Global),
            ],
            [
                CreateVariable("host", "api.example.test", VariableScope.Workspace),
            ],
            [],
            [
                CreateVariable("userId", "7", VariableScope.Runtime),
                CreateVariable("token", "abc123", VariableScope.Runtime),
            ]);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual("https://api.example.test/users/7?include=core", result.Value!.Uri.ToString());
        Assert.AreEqual("""{"id":"7"}""", result.Value.Body.RawContent);
        Assert.AreEqual("core", result.Value.Headers.Single(static item => item.Key == "X-Tenant").Value);
        Assert.AreEqual("Bearer abc123", result.Value.Headers.Single(static item => item.Key == "Authorization").Value);
        StringAssert.Contains(result.Value.RawRequest, "POST https://api.example.test/users/7?include=core");
    }

    [TestMethod]
    public void Prepare_places_api_key_in_query_string_when_requested()
    {
        var request = new RequestDefinition
        {
            UrlTemplate = "https://api.example.test/search?existing=1",
            Auth = new()
            {
                Mode = AuthMode.ApiKey,
                ApiKeyName = "api_key",
                ApiKeyValue = "secret",
                ApiKeyLocation = ApiKeyLocation.Query,
            },
        };

        var result = requestCompiler.Prepare(request, [], [], [], [], []);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual("existing=1&api_key=secret", result.Value!.Uri.Query.TrimStart('?'));
    }

    [TestMethod]
    public void Prepare_returns_failure_when_resolved_url_is_invalid()
    {
        var request = new RequestDefinition
        {
            UrlTemplate = "{{host}}/broken",
        };

        var result = requestCompiler.Prepare(
            request,
            [],
            [],
            [
                CreateVariable("host", "not a uri", VariableScope.Workspace),
            ],
            [],
            []);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.ErrorMessage ?? string.Empty, "invalid");
    }

    [TestMethod]
    public void Prepare_applies_basic_auth_using_rendered_credentials()
    {
        var request = new RequestDefinition
        {
            UrlTemplate = "https://api.example.test",
            Auth = new()
            {
                Mode = AuthMode.Basic,
                Username = "{{username}}",
                Password = "{{password}}",
            },
        };

        var result = requestCompiler.Prepare(
            request,
            [],
            [],
            [],
            [],
            [
                CreateVariable("username", "alice", VariableScope.Runtime),
                CreateVariable("password", "p@ss", VariableScope.Runtime),
            ]);

        Assert.IsTrue(result.Succeeded);
        var header = result.Value!.Headers.Single(static item => item.Key == "Authorization");
        Assert.AreEqual($"Basic {Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("alice:p@ss"))}", header.Value);
    }

    #endregion

    #region Private Methods

    private static VariableDefinition CreateVariable(string key, string value, VariableScope scope)
    {
        return new()
        {
            Key = key,
            Value = value,
            Scope = scope,
        };
    }

    #endregion
}
