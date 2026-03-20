namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class RoslynScriptEngineTests
{
    #region Private Fields

    private readonly RoslynScriptEngine scriptEngine = new(NullLogger<RoslynScriptEngine>.Instance);

    #endregion

    #region Public Methods

    [TestMethod]
    public async Task Run_updates_request_runtime_variables_tests_and_console()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                // Update the outgoing request and capture runtime data.
                request.SetHeader("X-Test", "ok");
                variables.Set("token", "123");
                tests.Equal(200, response.Status, "Status is 200.");
                console.Log("script-ran");
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                    Headers =
                    [
                        new()
                        {
                            Key = "Accept",
                            Value = "application/json",
                        },
                    ],
                    Body = new()
                    {
                        Mode = RequestBodyMode.Json,
                        RawContent = "{}",
                        ContentType = "application/json",
                    },
                },
                Response = new()
                {
                    StatusCode = 200,
                    Body = """{"ok":true}""",
                    ContentType = "application/json",
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("ok", result.PreparedRequest.Headers.Single(static item => item.Key == "X-Test").Value);
        Assert.AreEqual("123", result.RuntimeVariables.Single(static item => item.Key == "token").Value);
        Assert.AreEqual(TestOutcomeState.Passed, result.Tests.Single().State);
        Assert.AreEqual("script-ran", result.ConsoleEntries.Single().Message);
    }

    [TestMethod]
    public async Task Run_reports_script_failures_without_suppressing_test_results()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script = """tests.Fail("boom");""",
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });

        Assert.HasCount(1, result.Tests);
        Assert.AreEqual(TestOutcomeState.Failed, result.Tests.Single().State);
        StringAssert.Contains(result.ErrorMessage, "boom");
        Assert.AreEqual(ConsoleEntryLevel.Error, result.ConsoleEntries.Single().Level);
    }

    [TestMethod]
    public async Task Run_uses_last_variable_value_when_duplicate_keys_are_seeded()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                tests.Equal("workspace", variables.Get("shared_key"), "workspace value wins");
                variables.Set("runtime_only", "ok");
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
                GlobalVariables =
                [
                    new() { Key = "shared_key", Value = "global", Scope = VariableScope.Global },
                ],
                WorkspaceVariables =
                [
                    new() { Key = "shared_key", Value = "workspace", Scope = VariableScope.Workspace },
                ],
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(TestOutcomeState.Passed, result.Tests.Single(static item => item.Name == "workspace value wins").State);
        Assert.AreEqual("ok", result.RuntimeVariables.Single(static item => item.Key == "runtime_only").Value);
    }

    [TestMethod]
    public async Task Run_handles_duplicate_response_headers_without_throwing()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                tests.Equal("beta", response.Headers["X-Trace"], "last response header wins");
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Response = new()
                {
                    Headers =
                    [
                        new() { Key = "X-Trace", Value = "alpha" },
                        new() { Key = "X-Trace", Value = "beta" },
                    ],
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(TestOutcomeState.Passed, result.Tests.Single().State);
    }

    #endregion
}
