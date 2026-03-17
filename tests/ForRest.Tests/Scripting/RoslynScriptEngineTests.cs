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

    #endregion
}
