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
    public async Task Run_captures_stash_rows_from_dynamic_assignments_and_commit()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                stash.Method = request.Method;
                stash["Status Code"] = response.Status;
                stash.Commit();
                stash.Method = "POST";
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                    Method = HttpMethodKind.Get,
                },
                Response = new()
                {
                    StatusCode = 201,
                    Body = """{"ok":true}""",
                    ContentType = "application/json",
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "Method", "Status Code" }, result.Stash.Columns.ToArray());
        Assert.HasCount(2, result.Stash.Rows);
        Assert.AreEqual("GET", result.Stash.Rows[0].Values["Method"]);
        Assert.AreEqual("201", result.Stash.Rows[0].Values["Status Code"]);
        Assert.AreEqual("POST", result.Stash.Rows[1].Values["Method"]);
        var statusCode = result.Stash.Rows[1].Values.TryGetValue("Status Code", out var foundStatusCode) ? foundStatusCode : string.Empty;
        Assert.AreEqual(string.Empty, statusCode);
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
    public async Task Run_normalizes_response_aliases_and_supports_security_utilities()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                tests.Equal("Ada", response.userName, "can normalize json member names");
                tests.Equal("token-42", regex.Match("Bearer token-42", "Bearer\\s+([\\w-]+)", 1), "can regex capture");
                variables.Set("sha", crypto.Sha256("for-rest"));
                variables.Set("base64", encoding.Base64Encode("hello"));
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Response = new()
                {
                    StatusCode = 200,
                    Body = """{"user_name":"Ada"}""",
                    ContentType = "application/json",
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
        Assert.AreEqual("997660f633f3ce4f4a7e36300b2ddd185cf4e03df1e1549c6b42df065012a481", result.RuntimeVariables.Single(static item => item.Key == "sha").Value);
        Assert.AreEqual("aGVsbG8=", result.RuntimeVariables.Single(static item => item.Key == "base64").Value);
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
    public async Task Run_supports_lowercase_response_members_on_request_send_results()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                var sent = await request.send();
                tests.Equal(201, sent.status, "lowercase status works on sent result");
                tests.Equal("Ada", sent.userName, "normalized json members work on sent result");
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
                SendAsync = _ => Task.FromResult<ResponseSnapshot?>(
                    new()
                    {
                        StatusCode = 201,
                        Body = """{"user_name":"Ada"}""",
                        ContentType = "application/json",
                    }),
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
    }

    [TestMethod]
    public async Task Run_executes_compiled_forrest_flow_with_range_literals_and_length_aliases()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var compilation = compiler.Compile(
            """
            name "Natural Runtime"
            method GET
            url "https://api.example.test/users"
            max_send_iterations 2

            let attempts = [0..1]
            let sent = null

            foreach attempt in attempts
            {
              sent = request.send()
              if sent.status == 200 and sent.user.name.length() > 2
              {
                runtime last_attempt = attempt
                break
              }
            }

            if sent == null or sent.user.name.length() < 3
            {
              error "name length did not match"
            }
            """,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(compilation.Succeeded, string.Join(Environment.NewLine, compilation.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(compilation.Payload);

        var callbackCount = 0;
        var result = await scriptEngine.Run(
            new()
            {
                Script = compilation.Payload.Request.PreRequestScript,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test/users"),
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
                MaxSendIterations = 2,
                SendAsync = _ =>
                {
                    callbackCount++;
                    return Task.FromResult<ResponseSnapshot?>(
                        new()
                        {
                            StatusCode = 200,
                            Body = """{"user":{"name":"Ada"}}""",
                            ContentType = "application/json",
                        });
                },
            });

        Assert.AreEqual(1, callbackCount);
        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("0", result.RuntimeVariables.Single(static item => item.Key == "last_attempt").Value);
    }

    [TestMethod]
    public async Task Run_renders_dollar_brace_templates_when_sending_requests()
    {
        string? capturedUrl = null;
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                variables.Set("trace_id", "trace-42");
                request.Url = "https://api.example.test/${trace_id}";
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
                SendAsync = request =>
                {
                    capturedUrl = request.Uri.ToString();
                    return Task.FromResult<ResponseSnapshot?>(
                        new()
                        {
                            StatusCode = 200,
                            Body = """{"ok":true}""",
                            ContentType = "application/json",
                        });
                },
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("https://api.example.test/trace-42", capturedUrl);
    }

    [TestMethod]
    public async Task Run_request_send_returns_independent_response_objects()
    {
        var callbackCount = 0;
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                var first = await request.send();
                var second = await request.send();
                tests.Equal(201, first.Status, "first send status is preserved");
                tests.Equal("alpha", first.step, "first send json is preserved");
                tests.Equal(202, second.Status, "second send status is returned");
                tests.Equal("beta", second.step, "second send json is returned");
                tests.Equal(202, response.Status, "global response tracks latest send");
                tests.Equal("beta", response.step, "latest send json is exposed globally");
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
                MaxSendIterations = 2,
                SendAsync = _ =>
                {
                    callbackCount++;
                    return Task.FromResult<ResponseSnapshot?>(
                        callbackCount == 1
                            ? new()
                            {
                                StatusCode = 201,
                                Body = """{"step":"alpha"}""",
                                ContentType = "application/json",
                            }
                            : new()
                            {
                                StatusCode = 202,
                                Body = """{"step":"beta"}""",
                                ContentType = "application/json",
                            });
                },
            });

        Assert.AreEqual(2, callbackCount);
        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(202, result.Response?.StatusCode);
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
    }

    [TestMethod]
    public async Task Run_preserves_duplicate_request_headers_for_security_flows()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                request.AddHeader("X-Test", "alpha");
                request.AddHeader("X-Test", "beta");
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(2, result.PreparedRequest.Headers.Count(static item => item.Key == "X-Test"));
        Assert.AreEqual("alpha", result.PreparedRequest.Headers.First(static item => item.Key == "X-Test").Value);
        Assert.AreEqual("beta", result.PreparedRequest.Headers.Last(static item => item.Key == "X-Test").Value);
    }

    [TestMethod]
    public async Task Run_supports_top_level_response_array_iteration_and_indexing()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                var total = 0;
                foreach (var item in response)
                {
                    total += (int)item.id;
                }

                tests.Equal(30, total, "can iterate top-level arrays");
                tests.Equal(20, response[1].id, "can index top-level arrays");
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Response = new()
                {
                    StatusCode = 200,
                    Body = """[{"id":10},{"id":20}]""",
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
    public async Task Run_fails_fast_when_script_sets_invalid_request_url()
    {
        var callbackCount = 0;
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                request.Url = "not-a-valid-absolute-url";
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
                    return Task.FromResult<ResponseSnapshot?>(new());
                },
            });

        Assert.AreEqual(0, callbackCount);
        StringAssert.Contains(result.ErrorMessage, "valid absolute URL");
        Assert.AreEqual(ConsoleEntryLevel.Error, result.ConsoleEntries.Last().Level);
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

    [TestMethod]
    public async Task Run_request_send_renders_variable_templates_in_script_mutations()
    {
        PreparedRequest? capturedRequest = null;
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                request.Url = "https://api.example.test/users/{{user_id}}?trace={{trace_id}}";
                request.SetHeader("X-Trace", "{{trace_id}}");
                request.Body = "{\"id\":\"{{user_id}}\",\"trace\":\"{{trace_id}}\"}";
                await request.send();
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test/bootstrap"),
                    Body = new()
                    {
                        Mode = RequestBodyMode.Json,
                        ContentType = "application/json",
                        RawContent = "{}",
                    },
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
                RequestVariables =
                [
                    new() { Key = "user_id", Value = "42", Scope = VariableScope.RequestLocal },
                ],
                RuntimeVariables =
                [
                    new() { Key = "trace_id", Value = "trace-123", Scope = VariableScope.Runtime },
                ],
                MaxSendIterations = 1,
                SendAsync = preparedRequest =>
                {
                    capturedRequest = preparedRequest;
                    return Task.FromResult<ResponseSnapshot?>(
                        new()
                        {
                            StatusCode = 200,
                            Body = """{"ok":true}""",
                            ContentType = "application/json",
                        });
                },
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.IsNotNull(capturedRequest);
        Assert.AreEqual("https://api.example.test/users/42?trace=trace-123", capturedRequest.Uri.ToString());
        Assert.AreEqual("trace-123", capturedRequest.Headers.Single(static header => header.Key == "X-Trace").Value);
        StringAssert.Contains(capturedRequest.Body.RawContent, "\"id\":\"42\"");
        StringAssert.Contains(capturedRequest.RawRequest, "https://api.example.test/users/42?trace=trace-123");
    }

    [TestMethod]
    public async Task Run_does_not_consume_send_budget_when_request_cannot_be_prepared()
    {
        var callbackCount = 0;
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                request.Url = "not-a-valid-absolute-url";
                try
                {
                    await request.send();
                }
                catch
                {
                    console.Warn("invalid url rejected");
                }

                request.Url = "https://api.example.test/fixed";
                await request.send();
                tests.Equal(1, request.SendCount, "only successful sends consume budget");
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
        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(ConsoleEntryLevel.Warning, result.ConsoleEntries.First().Level);
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
    }

    #endregion
}
