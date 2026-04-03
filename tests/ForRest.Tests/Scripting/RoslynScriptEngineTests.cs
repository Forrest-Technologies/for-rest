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
    public async Task Run_reports_actionable_hint_for_null_runtime_binding_failures()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                var price = response.Json()["item"]["data"].price;
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Response = new()
                {
                    StatusCode = 200,
                    Body = """{"item":{"data":null}}""",
                    ContentType = "application/json",
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });

        StringAssert.Contains(result.ErrorMessage, "Cannot perform runtime binding on a null reference.");
        StringAssert.Contains(result.ErrorMessage, "if item.data != null");
        Assert.AreEqual(ConsoleEntryLevel.Error, result.ConsoleEntries.Single().Level);
        StringAssert.Contains(result.ConsoleEntries.Single().Message, "optional text fields");
    }

    [TestMethod]
    public async Task Run_treats_null_json_members_as_safe_dynamic_sentinels()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                tests.Equal("", convert.ToString(response.item.data.price), "null chain converts to empty text");
                tests.Equal(0d, convert.ToDouble(response.item.data.price), "null chain converts to zero");
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Response = new()
                {
                    StatusCode = 200,
                    Body = """{"item":{"data":null}}""",
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
    public async Task Run_supports_string_convert_and_time_helpers()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                var cleaned = strings.Trim("  Alpha Beta  ");
                var replaced = strings.Replace(cleaned, "beta", "Gamma", true);
                var pieces = strings.Split("one,,two", ",", true);
                var stamp = time.Parse("2026-03-29T10:15:00Z");
                var nextDay = time.AddDays(stamp, 1);
                var unix = time.UnixSeconds(stamp);

                tests.Equal("Alpha Gamma", replaced, "strings can trim and replace");
                tests.Equal("one|two", strings.Join("|", pieces), "strings can split and join");
                tests.Equal(true, convert.ToBool("yes"), "convert can coerce booleans");
                tests.Equal(42, convert.ToInt("42"), "convert can coerce ints");
                tests.Equal("42.5", convert.ToString(convert.ToDecimal("42.5")), "convert can round-trip decimals");
                tests.Equal("2026-03-30", time.Format(nextDay, "yyyy-MM-dd"), "time can add and format");
                tests.Equal("2026-03-29T10:15:00.0000000+00:00", time.Format(time.FromUnixSeconds(unix)), "time can round-trip unix seconds");
                variables.Set("unix", convert.ToString(unix));
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
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
        Assert.AreEqual("1774779300", result.RuntimeVariables.Single(static item => item.Key == "unix").Value);
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
        Assert.HasCount(1, result.SentResponses);
        Assert.AreEqual(201, result.SentResponses.Single().StatusCode);
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
    public async Task Run_executes_compiled_forrest_flow_with_convert_and_strings_helpers()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var compilation = compiler.Compile(
            """
            name "Filter Objects"
            method GET
            url "https://api.example.test/objects"

            let results_stashed = 0
            let max_results = 50

            foreach item in response {
              if results_stashed >= max_results {
                break
              }

              let name = item.name
              let price_num = convert.ToDouble(item.data.price)
              let first_upper = strings.Upper(strings.Substring(name, 0, 1))
              let letter_ok = strings.StartsWith(first_upper, "A") or strings.StartsWith(first_upper, "B") or strings.StartsWith(first_upper, "C")

              if letter_ok and price_num > 300 {
                stash.ID = item.id
                stash.Name = strings.Substring(name, 0, 10)
                stash.Price = price_num
                stash.Commit()
                results_stashed = results_stashed + 1
              }
            }
            """,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(compilation.Succeeded, string.Join(Environment.NewLine, compilation.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(compilation.Payload);

        var result = await scriptEngine.Run(
            new()
            {
                Script = compilation.Payload.Request.PreRequestScript,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test/objects"),
                },
                Response = new()
                {
                    StatusCode = 200,
                    Body =
                    """
                    [
                      { "id": "1", "name": "Acer Swift 5", "data": { "price": 1299.99 } },
                      { "id": "x", "name": "Beta Placeholder", "data": null },
                      { "id": "2", "name": "Dell XPS 13", "data": { "price": 999.99 } },
                      { "id": "3", "name": "Canon R5 Pro", "data": { "price": 3899 } },
                      { "id": "4", "name": "Beats Studio Pro", "data": { "price": 199.99 } }
                    ]
                    """,
                    ContentType = "application/json",
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "ID", "Name", "Price" }, result.Stash.Columns.ToArray());
        Assert.HasCount(2, result.Stash.Rows);
        Assert.AreEqual("1", result.Stash.Rows[0].Values["ID"]);
        Assert.AreEqual("Acer Swift", result.Stash.Rows[0].Values["Name"]);
        Assert.AreEqual("1299.99", result.Stash.Rows[0].Values["Price"]);
        Assert.AreEqual("3", result.Stash.Rows[1].Values["ID"]);
        Assert.AreEqual("Canon R5 P", result.Stash.Rows[1].Values["Name"]);
        Assert.AreEqual("3899", result.Stash.Rows[1].Values["Price"]);
    }

    [TestMethod]
    public async Task Run_executes_compiled_restful_api_surface_qa_script_through_pre_request_and_tests()
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var compilation = compiler.Compile(
            """
            name "restful-api QA"
            method GET
            url "https://api.restful-api.dev/objects"
            timeout 15000
            max_send_iterations 8
            redirects true
            ssl true
            history true

            runtime trace_id = guid()

            header "Accept" = "application/json"
            header "X-Workspace" = "{{workspace_name}}"
            header "X-Environment" = "{{environment_name}}"
            header "X-Correlation-Id" = "{{trace_id}}"

            let created_name = $"ForRest Widget {trace_id}"
            let patched_name = $"ForRest Widget Updated {trace_id}"

            log $"Trace {trace_id}: GET /objects"
            request.method = "GET"
            request.url = "https://api.restful-api.dev/objects"
            let sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "list returns 2xx")
            tests.Assert(sent.length() >= 3, "list returns at least 3 objects")
            let sample_id_a = convert.ToString(sent[0].id)
            let sample_id_b = convert.ToString(sent[1].id)
            let sample_id_c = convert.ToString(sent[2].id)
            stash.Step = "list"
            stash.Status = sent.status
            stash.Count = sent.length()
            stash.SampleIds = $"{sample_id_a},{sample_id_b},{sample_id_c}"
            stash.Trace = trace_id
            stash.Commit()

            log $"Trace {trace_id}: GET /objects?id=..."
            request.url = $"https://api.restful-api.dev/objects?id={sample_id_a}&id={sample_id_b}&id={sample_id_c}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "filtered list returns 2xx")
            tests.Equal(3, sent.length(), "filtered list returns requested ids")
            stash.Step = "filtered-list"
            stash.Status = sent.status
            stash.Count = sent.length()
            stash.FirstId = sent[0].id
            stash.Commit()

            log $"Trace {trace_id}: GET /objects/{sample_id_c}"
            request.url = $"https://api.restful-api.dev/objects/{sample_id_c}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "single object returns 2xx")
            tests.Equal(sample_id_c, convert.ToString(sent.id), "single object returns requested id")
            stash.Step = "single"
            stash.Status = sent.status
            stash.ObjectId = sent.id
            stash.Name = sent.name
            stash.Commit()

            log $"Trace {trace_id}: POST /objects"
            request.method = "POST"
            request.url = "https://api.restful-api.dev/objects"
            request.content_type = "application/json"
            request.body = $"{{\"name\":\"{created_name}\",\"data\":{{\"year\":2026,\"price\":1849.99,\"CPU model\":\"Trace CPU\",\"Hard disk size\":\"1 TB\"}}}}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "create returns 2xx")
            tests.Equal(created_name, convert.ToString(sent.name), "create echoes name")
            let created_id = sent.id
            stash.Step = "create"
            stash.Status = sent.status
            stash.ObjectId = created_id
            stash.Name = sent.name
            stash.CreatedAt = sent.createdAt
            stash.Commit()

            log $"Trace {trace_id}: PUT /objects/{created_id}"
            request.method = "PUT"
            request.url = $"https://api.restful-api.dev/objects/{created_id}"
            request.body = $"{{\"name\":\"{created_name}\",\"data\":{{\"year\":2026,\"price\":2049.99,\"CPU model\":\"Trace CPU\",\"Hard disk size\":\"1 TB\",\"color\":\"silver\"}}}}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "put returns 2xx")
            tests.Equal(2049.99, convert.ToDouble(sent.data.price), "put replaces price")
            tests.Equal("silver", convert.ToString(sent.data.color), "put adds color")
            stash.Step = "put"
            stash.Status = sent.status
            stash.ObjectId = sent.id
            stash.Price = sent.data.price
            stash.Color = sent.data.color
            stash.UpdatedAt = sent.updatedAt
            stash.Commit()

            log $"Trace {trace_id}: PATCH /objects/{created_id}"
            request.method = "PATCH"
            request.url = $"https://api.restful-api.dev/objects/{created_id}"
            request.body = $"{{\"name\":\"{patched_name}\"}}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "patch returns 2xx")
            tests.Equal(patched_name, convert.ToString(sent.name), "patch updates name")
            stash.Step = "patch"
            stash.Status = sent.status
            stash.ObjectId = sent.id
            stash.Name = sent.name
            stash.UpdatedAt = sent.updatedAt
            stash.Commit()

            log $"Trace {trace_id}: DELETE /objects/{created_id}"
            request.method = "DELETE"
            request.url = $"https://api.restful-api.dev/objects/{created_id}"
            request.body = ""
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "delete returns 2xx")
            tests.Assert(strings.Contains(convert.ToString(sent.message), convert.ToString(created_id)), "delete message includes id")
            stash.Step = "delete"
            stash.Status = sent.status
            stash.ObjectId = created_id
            stash.DeleteMessage = sent.message
            stash.Commit()

            expect status == 200 "final delete returns 200"
            expect header "Content-Type" contains "json" "json response"
            """,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(compilation.Succeeded, string.Join(Environment.NewLine, compilation.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(compilation.Payload);

        PreparedRequest initialRequest = BuildPreparedRequest(compilation.Payload.Request);
        var runtimeVariables = new ForRestRuntimeVariableSeedEvaluator().Evaluate(compilation.Payload.RuntimeSeeds);

        Task<ResponseSnapshot?> SendAsync(PreparedRequest preparedRequest)
        {
            return Task.FromResult<ResponseSnapshot?>(BuildRestfulApiDevResponse(preparedRequest));
        }

        ScriptExecutionResult preRequestResult = await scriptEngine.Run(
            new()
            {
                Script = compilation.Payload.Request.PreRequestScript,
                PreparedRequest = initialRequest,
                Response = BuildRestfulApiDevResponse(initialRequest),
                Workspace = new()
                {
                    Name = "Demo",
                },
                RequestVariables = [.. compilation.Payload.Request.Variables],
                RuntimeVariables = runtimeVariables,
                SendAsync = SendAsync,
                MaxSendIterations = compilation.Payload.Request.MaxSendIterations,
            });

        Assert.AreEqual(string.Empty, preRequestResult.ErrorMessage, compilation.Payload.Request.PreRequestScript);
        Assert.AreEqual(7, preRequestResult.SendCount);
        Assert.HasCount(7, preRequestResult.SentResponses);
        Assert.HasCount(7, preRequestResult.Stash.Rows);

        ScriptExecutionResult testsResult = await scriptEngine.Run(
            new()
            {
                Script = compilation.Payload.Request.TestsScript,
                PreparedRequest = preRequestResult.PreparedRequest,
                Response = preRequestResult.SentResponse ?? preRequestResult.Response,
                Workspace = new()
                {
                    Name = "Demo",
                },
                RequestVariables = [.. compilation.Payload.Request.Variables],
                RuntimeVariables = [.. preRequestResult.RuntimeVariables],
                SendAsync = SendAsync,
                MaxSendIterations = compilation.Payload.Request.MaxSendIterations,
            });

        Assert.AreEqual(string.Empty, testsResult.ErrorMessage, compilation.Payload.Request.TestsScript);
        Assert.IsTrue(testsResult.Tests.All(static item => item.State == TestOutcomeState.Passed));
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
        Assert.HasCount(2, result.SentResponses);
        Assert.AreEqual(201, result.SentResponses[0].StatusCode);
        Assert.AreEqual(202, result.SentResponses[1].StatusCode);
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
    }

    [TestMethod]
    public async Task Run_supports_workspace_execute_and_merges_nested_runtime_outputs()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script =
                """
                var auth = await workspace.execute("/requests/auth/token");
                tests.Equal(201, auth.status, "workspace execute returns response");
                tests.Equal("abc123", auth.token, "workspace execute exposes response json");
                tests.Equal("Bearer abc123", variables.Get("auth_header"), "nested runtime variables merge back");
                tests.Equal(201, response.Status, "global response tracks nested execute");
                """,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test/secure"),
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
                ExecuteWorkspaceRequestAsync = static (_, _) =>
                    Task.FromResult(
                        new ScriptExecutionResult
                        {
                            Response = new()
                            {
                                StatusCode = 201,
                                Body = """{"token":"abc123"}""",
                                ContentType = "application/json",
                            },
                            RuntimeVariables =
                            [
                                new()
                                {
                                    Key = "auth_header",
                                    Value = "Bearer abc123",
                                    Scope = VariableScope.Runtime,
                                },
                            ],
                            ConsoleEntries =
                            [
                                new()
                                {
                                    Level = ConsoleEntryLevel.Info,
                                    Message = "nested helper ran",
                                },
                            ],
                            Tests =
                            [
                                new()
                                {
                                    Name = "nested helper pass",
                                    Message = "nested helper pass",
                                    State = TestOutcomeState.Passed,
                                },
                            ],
                            Stash = new()
                            {
                                Columns = ["Token"],
                                Rows =
                                [
                                    new()
                                    {
                                        Values = new(StringComparer.OrdinalIgnoreCase)
                                        {
                                            ["Token"] = "abc123",
                                        },
                                    },
                                ],
                            },
                        }),
            });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(201, result.Response?.StatusCode);
        Assert.AreEqual("Bearer abc123", result.RuntimeVariables.Single(static item => item.Key == "auth_header").Value);
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
        Assert.IsTrue(result.ConsoleEntries.Any(static entry => entry.Message == "nested helper ran"));
        Assert.HasCount(1, result.Stash.Rows);
        Assert.AreEqual("abc123", result.Stash.Rows[0].Values["Token"]);
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

    private static PreparedRequest BuildPreparedRequest(RequestDefinition request)
    {
        return new()
        {
            Method = request.Method,
            Uri = Uri.TryCreate(request.UrlTemplate, UriKind.Absolute, out Uri? uri)
                ? uri
                : new Uri("https://localhost"),
            Headers = [.. request.Headers],
            Body = request.Body,
            Auth = request.Auth,
            TimeoutMilliseconds = request.TimeoutMilliseconds,
            FollowRedirects = request.FollowRedirects,
            ValidateSsl = request.ValidateSsl,
            RawRequest = $"{request.Method.ToString().ToUpperInvariant()} {request.UrlTemplate}",
        };
    }

    private static ResponseSnapshot BuildRestfulApiDevResponse(PreparedRequest preparedRequest)
    {
        string[] pathSegments = preparedRequest.Uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? objectId = pathSegments.Length > 1 ? pathSegments[1] : null;
        string timestamp = "2026-03-30T10:15:00.000Z";

        return preparedRequest.Method switch
        {
            HttpMethodKind.Get when !string.IsNullOrWhiteSpace(objectId) => BuildJsonResponse(
                200,
                new
                {
                    id = objectId,
                    name = objectId switch
                    {
                        "1" => "Google Pixel 6 Pro",
                        "2" => "Apple iPhone 12 Mini, 256GB, Blue",
                        _ => "Apple iPhone 12 Pro Max",
                    },
                    data = new
                    {
                        price = 1849.99,
                    },
                }),
            HttpMethodKind.Get when preparedRequest.Uri.Query.Contains("id=", StringComparison.OrdinalIgnoreCase) => BuildJsonResponse(
                200,
                GetQueryValues(preparedRequest.Uri, "id")
                    .Select(
                        id => new
                        {
                            id,
                            name = $"Object {id}",
                            data = new
                            {
                                price = 1849.99,
                            },
                        })
                    .ToArray()),
            HttpMethodKind.Get => BuildJsonResponse(
                200,
                new object[]
                {
                    new
                    {
                        id = "1",
                        name = "Google Pixel 6 Pro",
                        data = new
                        {
                            price = 999.99,
                        },
                    },
                    new
                    {
                        id = "2",
                        name = "Apple iPhone 12 Mini, 256GB, Blue",
                        data = (object?)null,
                    },
                    new
                    {
                        id = "3",
                        name = "Apple iPhone 12 Pro Max",
                        data = new
                        {
                            price = 1849.99,
                        },
                    },
                }),
            HttpMethodKind.Post => BuildJsonResponse(
                201,
                new
                {
                    id = "701",
                    name = ReadJsonString(preparedRequest.Body.RawContent, "name") ?? "Validation Object",
                    createdAt = timestamp,
                }),
            HttpMethodKind.Put => BuildJsonResponse(
                200,
                new
                {
                    id = objectId ?? "701",
                    name = ReadJsonString(preparedRequest.Body.RawContent, "name") ?? "Validation Object",
                    data = new
                    {
                        price = ReadJsonNumber(preparedRequest.Body.RawContent, "data", "price") ?? 2049.99,
                        color = ReadJsonString(preparedRequest.Body.RawContent, "data", "color") ?? "silver",
                    },
                    updatedAt = timestamp,
                }),
            HttpMethodKind.Patch => BuildJsonResponse(
                200,
                new
                {
                    id = objectId ?? "701",
                    name = ReadJsonString(preparedRequest.Body.RawContent, "name") ?? "Validation Object Updated",
                    updatedAt = timestamp,
                }),
            HttpMethodKind.Delete => BuildJsonResponse(
                200,
                new
                {
                    message = $"Object with id = {objectId ?? "701"}, has been deleted.",
                }),
            _ => BuildJsonResponse(200, new { ok = true }),
        };
    }

    private static ResponseSnapshot BuildJsonResponse<T>(int statusCode, T body)
    {
        string bodyJson = System.Text.Json.JsonSerializer.Serialize(body);
        return new()
        {
            StatusCode = statusCode,
            ReasonPhrase = statusCode == 201 ? "Created" : "OK",
            ContentType = "application/json",
            Body = bodyJson,
            RawResponse = $"HTTP/1.1 {statusCode}",
            SizeBytes = bodyJson.Length,
            Headers =
            [
                new()
                {
                    Key = "Content-Type",
                    Value = "application/json",
                },
            ],
        };
    }

    private static string[] GetQueryValues(Uri uri, string key)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return [];
        }

        return uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(
                static pair =>
                {
                    string[] parts = pair.Split('=', 2);
                    return new
                    {
                        Key = Uri.UnescapeDataString(parts[0]),
                        Value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty,
                    };
                })
            .Where(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.Value)
            .ToArray();
    }

    private static string? ReadJsonString(string json, params string[] path)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement current = document.RootElement;
        foreach (string segment in path)
        {
            if (current.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == System.Text.Json.JsonValueKind.String
            ? current.GetString()
            : current.ToString();
    }

    private static double? ReadJsonNumber(string json, params string[] path)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement current = document.RootElement;
        foreach (string segment in path)
        {
            if (current.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == System.Text.Json.JsonValueKind.Number &&
               current.TryGetDouble(out double number)
            ? number
            : null;
    }

    #endregion
}
