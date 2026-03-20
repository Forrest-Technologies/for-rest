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

    [TestMethod]
    public async Task Run_exposes_response_json_members_as_dynamic_properties()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                tests.Equal("Ada", response.user.name, "can read nested object");
                var total = 0;
                foreach (var item in response.items)
                {
                    total += (int)item.id;
                }
                tests.Equal(30, total, "can iterate arrays");
                tests.Equal(20, response.items[1].id, "can index arrays");
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Response = new()
                {
                    StatusCode = 200,
                    Body = """{"user":{"name":"Ada"},"items":[{"id":10},{"id":20}]}""",
                    ContentType = "application/json",
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
    }

    [TestMethod]
    public async Task Run_supports_request_send_and_updates_global_response()
    {
        var callbackCount = 0;
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                await request.send();
                tests.Equal(201, response.Status, "send updates response status");
                tests.Equal("Ada", response.user.name, "send updates response json");
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
                MaxSendIterations = 1,
                SendAsync = _ =>
                {
                    callbackCount++;
                    return Task.FromResult<ResponseSnapshot?>(
                        new()
                        {
                            StatusCode = 201,
                            Body = """{"user":{"name":"Ada"}}""",
                            ContentType = "application/json",
                        });
                },
            });

        Assert.AreEqual(1, callbackCount);
        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(201, result.Response?.StatusCode);
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
    }

    [TestMethod]
    public async Task Run_fails_when_request_send_exceeds_max_iterations()
    {
        var callbackCount = 0;
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                await request.send();
                await request.send();
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
                MaxSendIterations = 1,
                SendAsync = _ =>
                {
                    callbackCount++;
                    return Task.FromResult<ResponseSnapshot?>(
                        new()
                        {
                            StatusCode = 200,
                            Body = """{"ok":true}""",
                            ContentType = "application/json",
                        });
                },
            });

        Assert.AreEqual(1, callbackCount);
        StringAssert.Contains(result.ErrorMessage, "max_send_iterations");
        Assert.AreEqual(ConsoleEntryLevel.Error, result.ConsoleEntries.Last().Level);
    }

    #endregion
}
