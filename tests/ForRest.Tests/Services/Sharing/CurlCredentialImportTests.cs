using ForRest.Mcp;
using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class CurlCredentialImportTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("--user 'reader:fake-password-9182'", "Authorization", "Basic cmVhZGVyOmZha2UtcGFzc3dvcmQtOTE4Mg==", "fake-password-9182")]
    [DataRow("--oauth2-bearer fake-bearer-7264", "Authorization", "Bearer fake-bearer-7264", "fake-bearer-7264")]
    [DataRow("--header 'Authorization: Bearer fake-header-5382'", "Authorization", "Bearer fake-header-5382", "fake-header-5382")]
    [DataRow("--header 'X-Api-Key: fake-api-key-2468'", "X-Api-Key", "fake-api-key-2468", "fake-api-key-2468")]
    [DataRow("--header 'X-Credential: fake-credential-6712'", "X-Credential", "fake-credential-6712", "fake-credential-6712")]
    [DataRow("--cookie 'session=fake-cookie-3918'", "Cookie", "session=fake-cookie-3918", "fake-cookie-3918")]
    public void Imported_credentials_are_secret_seeds_and_resolve_to_the_original_header(
        string options, string headerName, string expectedHeader, string credential)
    {
        var source = new CurlImportService().ConvertToScript($"curl {options} https://fixture.test/", out _);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
        Assert.IsTrue(compilation.Succeeded, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        var payload = compilation.Payload!;
        var runtime = new ForRestRuntimeVariableSeedEvaluator().Evaluate(payload.RuntimeSeeds);

        var prepared = new RequestCompiler(new VariableResolver()).Prepare(payload.Request, [], [], [], [], runtime);

        Assert.IsTrue(prepared.Succeeded, prepared.ErrorMessage);
        Assert.AreEqual(expectedHeader, prepared.Value!.Headers.Single(item => item.Key == headerName).Value);
        Assert.IsTrue(runtime.Any(variable => variable.IsSecret && variable.Value.Contains(credential, StringComparison.Ordinal)));
        Assert.DoesNotContain(credential, McpSecretRedactor.Redact(source));
        Assert.DoesNotContain(credential, FrsSecretRedactor.Redact(source));
    }

    [TestMethod]
    public void Separate_sensitive_headers_get_distinct_variables_and_public_headers_stay_literal()
    {
        var source = new CurlImportService().ConvertToScript(
            "curl -H 'X-Api-Key: fake-key-one' -H 'X-Api-Key: fake-key-two' -H 'Accept: application/json' https://fixture.test/", out _);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
        Assert.IsTrue(compilation.Succeeded);
        var payload = compilation.Payload!;
        var runtime = new ForRestRuntimeVariableSeedEvaluator().Evaluate(payload.RuntimeSeeds);

        var prepared = new RequestCompiler(new VariableResolver()).Prepare(payload.Request, [], [], [], [], runtime);

        Assert.IsTrue(prepared.Succeeded);
        CollectionAssert.AreEqual(new[] { "fake-key-one", "fake-key-two" },
            prepared.Value!.Headers.Where(item => item.Key == "X-Api-Key").Select(item => item.Value).ToArray());
        Assert.AreEqual(2, runtime.Count(variable => variable.IsSecret));
        Assert.AreEqual(2, runtime.Select(variable => variable.Key).Distinct(StringComparer.Ordinal).Count());
        StringAssert.Contains(source, "header \"Accept\" = \"application/json\"");
    }

    [TestMethod]
    [DataRow("--user 'fake-api-key-4321:'", "fake-api-key-4321", "")]
    [DataRow("-u $'fake\\tuser:fake-password'", "fake\tuser", "fake-password")]
    [DataRow("--user 'fake-üser:fake:password'", "fake-üser", "fake:password")]
    [DataRow("--user ':fake-password'", "", "fake-password")]
    public void Both_basic_authentication_fields_are_protected_and_preserved(
        string options, string username, string password)
    {
        var source = new CurlImportService().ConvertToScript($"curl {options} https://fixture.test/", out _);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
        Assert.IsTrue(compilation.Succeeded, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        var payload = compilation.Payload!;
        var runtime = new ForRestRuntimeVariableSeedEvaluator().Evaluate(payload.RuntimeSeeds);

        var prepared = new RequestCompiler(new VariableResolver()).Prepare(payload.Request, [], [], [], [], runtime);

        Assert.IsTrue(prepared.Succeeded, prepared.ErrorMessage);
        Assert.AreEqual("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")),
            prepared.Value!.Headers.Single(item => item.Key == "Authorization").Value);
        Assert.AreEqual(username, runtime.Single(variable => variable.Key == "curl_username" && variable.IsSecret).Value);
        Assert.AreEqual(password, runtime.Single(variable => variable.Key == "curl_password" && variable.IsSecret).Value);
        foreach (var credential in new[] { username, password }.Where(value => value.Length > 0))
        {
            Assert.DoesNotContain(credential, McpSecretRedactor.Redact(source));
            Assert.DoesNotContain(credential, FrsSecretRedactor.Redact(source));
        }
    }

    [TestMethod]
    public void Secret_passwords_preserve_shell_decoded_control_characters()
    {
        var source = new CurlImportService().ConvertToScript("curl -u $'reader:fake\\tpassword' https://fixture.test/", out _);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
        Assert.IsTrue(compilation.Succeeded);
        var payload = compilation.Payload!;
        var runtime = new ForRestRuntimeVariableSeedEvaluator().Evaluate(payload.RuntimeSeeds);

        var prepared = new RequestCompiler(new VariableResolver()).Prepare(payload.Request, [], [], [], [], runtime);

        Assert.IsTrue(prepared.Succeeded);
        Assert.AreEqual("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("reader:fake\tpassword")),
            prepared.Value!.Headers.Single(item => item.Key == "Authorization").Value);
        StringAssert.Contains(runtime.Single(variable => variable.Key == "curl_password").Value, "\t");
    }

    #endregion
}
