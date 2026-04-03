using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ForRest.Tests.Services;

[TestClass]
public sealed class ForRestScriptExecutionServiceTests
{
    [TestMethod]
    public async Task Execute_renders_runtime_seeds_before_request_compilation_and_captures_results()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(listener, 1, static _ => 201);

        try
        {
            var workspaceId = Guid.NewGuid();
            var executionService = CreateService();
            var source =
                """"
                name "Scripted Echo"
                method POST
                url "http://127.0.0.1:__PORT__/echo/{{resource_id}}?trace={{trace_id}}"
                content_type "application/json"
                history false

                request resource_id = "42"
                runtime trace_id = "trace-123"

                header "Accept" = "application/json"
                header "X-Trace-Id" = "{{trace_id}}"

                body json """
                {
                  "id": "{{resource_id}}",
                  "trace": "{{trace_id}}"
                }
                """

                extract runtime echoed_id = json "$.payload.id"

                let sent = request.send()

                expect status == 201 "returns 201"
                expect json "$.payload.trace" == "trace-123" "trace matches"
                expect header "Content-Type" contains "application/json" "content type matches"
                """".Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = workspaceId,
                        Name = "Demo",
                    },
                },
                source,
                null);

            var capturedRequest = (await requestCaptureTask).Single();

            Assert.IsTrue(
                result.Compilation.Succeeded,
                string.Join(Environment.NewLine, result.Compilation.Diagnostics.Select(static item => item.Message)));
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual(
                ExecutionState.Completed,
                result.Execution.State,
                result.Execution.Runs.Single().ErrorMessage);
            Assert.AreEqual("/echo/42?trace=trace-123", capturedRequest.PathAndQuery);
            Assert.AreEqual($"http://127.0.0.1:{port}/echo/42?trace=trace-123", result.Execution.Runs.Single().TargetUri);
            Assert.AreEqual("trace-123", capturedRequest.Headers["X-Trace-Id"]);
            StringAssert.Contains(capturedRequest.Body, "\"trace\": \"trace-123\"");
            Assert.AreEqual("42", result.Execution.RuntimeVariables.Single(static item => item.Key == "echoed_id").Value);
            Assert.IsTrue(result.Execution.Tests.All(static item => item.State == TestOutcomeState.Passed));
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_renders_workspace_execute_results_inside_request_templates()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(
            listener,
            2,
            static _ => 200,
            attempt => attempt == 1
                ? """{"uuid":"70078e36-d48d-4b28-bb43-79ba9e9864ba"}"""
                : """{"ok":true}""");

        try
        {
            var workspaceId = Guid.NewGuid();
            var executionService = CreateService();
            var helperSource =
                """
                name "Get UUID"
                method GET
                url "http://127.0.0.1:__PORT__/uuid"
                history false
                max_send_iterations 1

                let sent = request.send()

                expect status == 200 "returns 200"
                """.Replace("__PORT__", port.ToString());
            var helperCompilation = executionService.Compile(helperSource, workspaceId, "Get UUID");
            Assert.IsTrue(
                helperCompilation.Succeeded,
                string.Join(Environment.NewLine, helperCompilation.Diagnostics.Select(static item => item.Message)));

            var source =
                """"
                name "Post Echo"
                method POST
                url "http://127.0.0.1:__PORT__/post"
                timeout 15000
                max_send_iterations 1
                redirects true
                ssl true
                history false
                content_type "application/json"

                let uuid = workspace.execute("Get UUID")

                header "Accept" = "application/json"
                header "X-UUID-Test" = "{{uuid.uuid}}"

                body json """
                {
                  "request": "Post Echo",
                  "guid": "{{uuid.uuid}}"
                }
                """

                let sent = request.send()

                expect status == 200 "returns 200"
                """".Replace("__PORT__", port.ToString());

            var workspace = new WorkspaceSnapshot
            {
                Workspace = new()
                {
                    Id = workspaceId,
                    Name = "Template Demo",
                },
                Nodes =
                [
                    new()
                    {
                        WorkspaceId = workspaceId,
                        Kind = WorkspaceNodeKind.Request,
                        Name = "Get UUID",
                        Location = "/requests/helpers/get-uuid",
                        SortOrder = 0,
                        Request = helperCompilation.Payload?.Request,
                    },
                ],
            };

            var result = await executionService.Execute(new(), workspace, source, null);
            var capturedRequests = await WaitForRequestsAsync(requestCaptureTask);

            Assert.IsTrue(
                result.Compilation.Succeeded,
                string.Join(Environment.NewLine, result.Compilation.Diagnostics.Select(static item => item.Message)));
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual(ExecutionState.Completed, result.Execution.State, result.Execution.Runs.Single().ErrorMessage);
            Assert.HasCount(2, capturedRequests);
            Assert.AreEqual("/uuid", capturedRequests[0].PathAndQuery);
            Assert.AreEqual("/post", capturedRequests[1].PathAndQuery);
            Assert.AreEqual("70078e36-d48d-4b28-bb43-79ba9e9864ba", capturedRequests[1].Headers["X-UUID-Test"]);
            StringAssert.Contains(capturedRequests[1].Body, "\"guid\": \"70078e36-d48d-4b28-bb43-79ba9e9864ba\"");
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_runs_request_send_from_top_level_frs_code()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(listener, 1, static _ => 200);

        try
        {
            var executionService = CreateService();
            var source =
                """
                name "Flow Send"
                method GET
                url "http://127.0.0.1:__PORT__/flow"
                history false
                max_send_iterations 2

                request.headers["X-Flow-Step"] = "ran"
                let sent = request.send()
                if response.attempt == 1 {
                  runtime probe_attempt = response.attempt
                }

                expect status == 200 "returns 200"
                expect header "Content-Type" contains "application/json" "json response"
                """.Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Flow Demo",
                    },
                },
                source,
                null);

            Assert.IsTrue(
                result.Compilation.Succeeded,
                string.Join(Environment.NewLine, result.Compilation.Diagnostics.Select(static item => item.Message)));
            Assert.IsNotNull(result.Execution);
            if (result.Execution.State != ExecutionState.Completed)
            {
                Assert.Fail(
                    string.Join(
                        Environment.NewLine,
                        [
                            $"State: {result.Execution.State}",
                            $"Error: {result.Execution.Runs.Single().ErrorMessage}",
                            $"Response: {result.Execution.LatestResponse?.StatusCode}",
                            $"Tests: {string.Join(", ", result.Execution.Tests.Select(static test => $"{test.Name}:{test.State}"))}",
                            $"Console: {string.Join(" | ", result.Execution.ConsoleEntries.Select(static entry => $"{entry.Level}:{entry.Message}"))}"
                        ]));
            }

            var capturedRequest = (await WaitForRequestsAsync(requestCaptureTask)).Single();
            Assert.AreEqual("ran", capturedRequest.Headers["X-Flow-Step"]);
            Assert.AreEqual("1", result.Execution.RuntimeVariables.Single(static item => item.Key == "probe_attempt").Value);
            StringAssert.Contains(result.Execution.LatestResponse?.Body ?? string.Empty, "\"attempt\":1");
            Assert.IsTrue(result.Execution.Tests.All(static item => item.State == TestOutcomeState.Passed));
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_captures_stash_rows_from_top_level_flow_code()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(listener, 2, attempt => attempt == 1 ? 500 : 200);

        try
        {
            var executionService = CreateService();
            var source =
                """
                name "Flow Stash"
                method GET
                url "http://127.0.0.1:__PORT__/stash"
                history false
                max_send_iterations 3

                let sent = null
                foreach attempt in [0..2] {
                  sent = request.send()
                  stash.Index = attempt
                  stash.HttpStatus = sent.status
                  stash.ServerAttempt = sent.attempt
                  stash.Commit()
                  if sent.status == 200 {
                    break
                  }
                }

                expect status == 200 "returns 200"
                """.Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Flow Stash Demo",
                    },
                },
                source,
                null);

            var capturedRequests = await WaitForRequestsAsync(requestCaptureTask);

            Assert.IsTrue(
                result.Compilation.Succeeded,
                string.Join(Environment.NewLine, result.Compilation.Diagnostics.Select(static item => item.Message)));
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual(ExecutionState.Completed, result.Execution.State, result.Execution.Runs.Single().ErrorMessage);
            Assert.HasCount(2, capturedRequests);
            CollectionAssert.AreEqual(new[] { "Index", "HttpStatus", "ServerAttempt" }, result.Execution.Stash.Columns.ToArray());
            Assert.HasCount(2, result.Execution.Stash.Rows);
            Assert.AreEqual("0", result.Execution.Stash.Rows[0].Values["Index"]);
            Assert.AreEqual("500", result.Execution.Stash.Rows[0].Values["HttpStatus"]);
            Assert.AreEqual("1", result.Execution.Stash.Rows[0].Values["ServerAttempt"]);
            Assert.AreEqual("1", result.Execution.Stash.Rows[1].Values["Index"]);
            Assert.AreEqual("200", result.Execution.Stash.Rows[1].Values["HttpStatus"]);
            Assert.AreEqual("2", result.Execution.Stash.Rows[1].Values["ServerAttempt"]);
            Assert.HasCount(2, result.Execution.Runs.Single().Stash.Rows);
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_accepts_semicolon_heavy_mixed_style_scripts_without_parse_failures()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(listener, 1, static _ => 200);

        try
        {
            var executionService = CreateService();
            var source =
                """
                name "Mixed Script";
                method GET;
                url "http://127.0.0.1:__PORT__/mixed/{{resource_id}}";
                history false;
                max_send_iterations 1;

                request resource_id = "42";
                request.SetHeader("X-Mode", "mixed");
                console.Log("Prepared request before send.");
                let sent = request.send();
                if (response.status == 200) {
                  runtime attempt_seen = response.attempt;
                }

                expect status == 200 "returns 200";
                expect header "Content-Type" contains "json" "json response";
                """.Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Mixed Demo",
                    },
                },
                source,
                null);

            var capturedRequest = (await WaitForRequestsAsync(requestCaptureTask)).Single();

            Assert.IsTrue(
                result.Compilation.Succeeded,
                string.Join(Environment.NewLine, result.Compilation.Diagnostics.Select(static item => item.Message)));
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual(ExecutionState.Completed, result.Execution.State, result.Execution.Runs.Single().ErrorMessage);
            Assert.AreEqual("/mixed/42", capturedRequest.PathAndQuery);
            Assert.AreEqual("mixed", capturedRequest.Headers["X-Mode"]);
            Assert.AreEqual("1", result.Execution.RuntimeVariables.Single(static item => item.Key == "attempt_seen").Value);
            Assert.IsTrue(result.Execution.Tests.All(static item => item.State == TestOutcomeState.Passed));
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_supports_indexed_collection_access_on_request_send_results()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(
            listener,
            1,
            static _ => 200,
            static _ =>
                """
                [
                  {
                    "email": "alpha@example.test",
                    "username": "alpha"
                  },
                  {
                    "email": "beta@example.test",
                    "username": "beta"
                  }
                ]
                """);

        try
        {
            var executionService = CreateService();
            var source =
                """
                name "Users Feed"
                method GET
                url "http://127.0.0.1:__PORT__/requests/users/list/42"
                history false
                max_send_iterations 3

                request.headers["X-Request-Source"] = "maui"
                let sent = request.send()

                if sent.status == 200 {
                  runtime first_user_email = sent[0].email
                  foreach index in range(0, 2) {
                    log sent[index].username
                  }
                } else {
                  warn sent.status
                }

                expect status == 200 "returns 200"
                expect header "Content-Type" contains "json" "json response"
                """.Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Users Feed Demo",
                    },
                },
                source,
                null);

            var capturedRequest = (await WaitForRequestsAsync(requestCaptureTask)).Single();

            Assert.IsTrue(
                result.Compilation.Succeeded,
                string.Join(Environment.NewLine, result.Compilation.Diagnostics.Select(static item => item.Message)));
            Assert.IsNotNull(result.Execution);
            if (result.Execution.State != ExecutionState.Completed)
            {
                Assert.Fail(
                    string.Join(
                        Environment.NewLine,
                        [
                            $"State: {result.Execution.State}",
                            $"Error: {result.Execution.Runs.Single().ErrorMessage}",
                            $"Response: {result.Execution.LatestResponse?.StatusCode}",
                            $"Console: {string.Join(" | ", result.Execution.ConsoleEntries.Select(static entry => $"{entry.Level}:{entry.Message}"))}"
                        ]));
            }

            Assert.AreEqual("/requests/users/list/42", capturedRequest.PathAndQuery);
            Assert.AreEqual("alpha@example.test", result.Execution.RuntimeVariables.Single(static item => item.Key == "first_user_email").Value);
            CollectionAssert.AreEqual(
                new[] { "alpha", "beta" },
                result.Execution.ConsoleEntries.Select(static entry => entry.Message).Where(static message => !string.IsNullOrWhiteSpace(message)).ToArray());
            Assert.IsTrue(result.Execution.Tests.All(static item => item.State == TestOutcomeState.Passed));
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_retries_on_server_errors_when_retry_block_is_present()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(listener, 2, attempt => attempt == 1 ? 500 : 200);

        try
        {
            var executionService = CreateService();
            var source =
                """"
                request {
                  method = GET
                  url = "http://127.0.0.1:__PORT__/retry"
                  history = false
                }

                tests {
                  status == 200 "returns 200 after retry"
                }

                retry {
                  count = 1
                  interval = 0
                }
                """".Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Retry Demo",
                    },
                },
                source,
                null);

            var capturedRequests = await requestCaptureTask;

            Assert.IsTrue(result.Compilation.Succeeded);
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual(ExecutionState.Completed, result.Execution.State);
            Assert.HasCount(2, capturedRequests);
            Assert.AreEqual(200, result.Execution.LatestResponse?.StatusCode);
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_runs_pre_request_script_override_against_compiled_request()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(listener, 1, static _ => 200);

        try
        {
            var executionService = CreateService();
            var source =
                """
                meta {
                  name = "Header Override"
                }

                request {
                  method = GET
                  url = "http://127.0.0.1:__PORT__/headers"
                  history = false
                }

                tests {
                  status == 200 "returns 200"
                }
                """.Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Script Override Demo",
                    },
                },
                source,
                null,
                preRequestScriptOverride: """request.SetHeader("X-Script-Override", "true");""");

            var capturedRequest = (await requestCaptureTask).Single();

            Assert.IsTrue(result.Compilation.Succeeded);
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual("true", capturedRequest.Headers["X-Script-Override"]);
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_allows_pre_request_script_to_send_and_inspect_response()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(listener, 1, static _ => 200);

        try
        {
            var executionService = CreateService();
            var source =
                """
                meta {
                  name = "Probe Send"
                }

                request {
                  method = GET
                  url = "http://127.0.0.1:__PORT__/probe"
                  history = false
                }

                tests {
                  status == 200 "returns 200"
                }
                """.Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Probe Demo",
                    },
                },
                source,
                null,
                preRequestScriptOverride:
                """
                await request.send();
                variables.Set("probe_attempt", response.attempt.ToString());
                """);

            var capturedRequests = await requestCaptureTask;

            Assert.IsTrue(result.Compilation.Succeeded);
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual(ExecutionState.Completed, result.Execution.State);
            Assert.HasCount(1, capturedRequests);
            Assert.AreEqual("1", result.Execution.RuntimeVariables.Single(static item => item.Key == "probe_attempt").Value);
            StringAssert.Contains(result.Execution.LatestResponse?.Body ?? string.Empty, "\"attempt\":1");
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_allows_script_to_capture_each_send_and_uses_last_send_for_run_output()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(listener, 2, static _ => 200);

        try
        {
            var executionService = CreateService();
            var source =
                """
                meta {
                  name = "Multi Send"
                }

                request {
                  method = GET
                  url = "http://127.0.0.1:__PORT__/probe?step=1"
                  history = false
                  max_send_iterations = 3
                }

                tests {
                  status == 200 "returns 200"
                }
                """.Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Multi Demo",
                    },
                },
                source,
                null,
                preRequestScriptOverride:
                """
                var first = await request.send();
                variables.Set("first_attempt", first.attempt.ToString());
                request.Url = request.Url.Replace("step=1", "step=2", StringComparison.Ordinal);
                var second = await request.send();
                variables.Set("second_attempt", second.attempt.ToString());
                """);

            var capturedRequests = await requestCaptureTask;
            ExecutionRun run = result.Execution!.Runs.Single();

            Assert.IsTrue(result.Compilation.Succeeded);
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual(ExecutionState.Completed, result.Execution.State);
            Assert.HasCount(2, capturedRequests);
            Assert.AreEqual("/probe?step=1", capturedRequests[0].PathAndQuery);
            Assert.AreEqual("/probe?step=2", capturedRequests[1].PathAndQuery);
            Assert.AreEqual("1", result.Execution.RuntimeVariables.Single(static item => item.Key == "first_attempt").Value);
            Assert.AreEqual("2", result.Execution.RuntimeVariables.Single(static item => item.Key == "second_attempt").Value);
            Assert.AreEqual($"http://127.0.0.1:{port}/probe?step=2", run.TargetUri);
            Assert.HasCount(2, run.Requests);
            Assert.AreEqual("GET", run.Requests[0].Method);
            Assert.AreEqual($"http://127.0.0.1:{port}/probe?step=1", run.Requests[0].Url);
            Assert.AreEqual($"http://127.0.0.1:{port}/probe?step=2", run.Requests[1].Url);
            StringAssert.Contains(run.Requests[0].RawRequest, "/probe?step=1");
            StringAssert.Contains(run.Requests[1].RawRequest, "/probe?step=2");
            StringAssert.Contains(result.Execution.LatestResponse?.Body ?? string.Empty, "\"attempt\":2");
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_preserves_last_send_details_when_pre_request_script_fails_after_send()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(listener, 1, static _ => 200);

        try
        {
            var executionService = CreateService();
            var source =
                """
                request {
                  method = GET
                  url = "http://127.0.0.1:__PORT__/boom"
                  history = false
                }
                """.Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Failure Demo",
                    },
                },
                source,
                null,
                preRequestScriptOverride:
                """
                await request.send();
                throw new InvalidOperationException("boom after send");
                """);

            var capturedRequests = await requestCaptureTask;
            ExecutionRun run = result.Execution!.Runs.Single();

            Assert.IsTrue(result.Compilation.Succeeded);
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual(ExecutionState.Failed, result.Execution.State);
            Assert.HasCount(1, capturedRequests);
            Assert.AreEqual($"http://127.0.0.1:{port}/boom", run.TargetUri);
            Assert.IsNotNull(run.Response);
            StringAssert.Contains(run.ErrorMessage, "boom after send");
            StringAssert.Contains(run.Response.Body, "\"attempt\":1");
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_fails_when_script_send_exceeds_max_send_iterations()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCaptureTask = CaptureRequests(listener, 1, static _ => 200);

        try
        {
            var executionService = CreateService();
            var source =
                """
                request {
                  method = GET
                  url = "http://127.0.0.1:__PORT__/guarded"
                  history = false
                  max_send_iterations = 1
                }
                """.Replace("__PORT__", port.ToString());

            var result = await executionService.Execute(
                new(),
                new()
                {
                    Workspace = new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Guard Demo",
                    },
                },
                source,
                null,
                preRequestScriptOverride:
                """
                await request.send();
                await request.send();
                """);

            var capturedRequests = await requestCaptureTask;

            Assert.IsTrue(result.Compilation.Succeeded);
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual(ExecutionState.Failed, result.Execution.State);
            Assert.HasCount(1, capturedRequests);
            StringAssert.Contains(result.Execution.Runs.Single().ErrorMessage, "max_send_iterations");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static IForRestScriptExecutionService CreateService()
    {
        return new ForRestScriptExecutionService(
            new ForRestScriptCompiler(new ForRestScriptParser()),
            new ForRestRuntimeVariableSeedEvaluator(),
            new RequestExecutionService(
                new RequestCompiler(new VariableResolver()),
                new ResponseExtractionService(),
                new RecordingExecutionHistoryRepository(),
                new RepeatRunnerService(),
                new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance),
                NullLogger<RequestExecutionService>.Instance));
    }

    private static async Task<List<CapturedRequest>> CaptureRequests(
        TcpListener listener,
        int requestCount,
        Func<int, int> statusCodeSelector,
        Func<int, string>? responseBodySelector = null,
        CancellationToken cancellationToken = default)
    {
        var requests = new List<CapturedRequest>();

        for (var attempt = 1; attempt <= requestCount; attempt++)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } headerLine)
            {
                var separatorIndex = headerLine.IndexOf(':');
                if (separatorIndex < 0)
                {
                    continue;
                }

                headers[headerLine[..separatorIndex]] = headerLine[(separatorIndex + 1)..].Trim();
            }

            var contentLength = headers.TryGetValue("Content-Length", out var rawContentLength)
                && int.TryParse(rawContentLength, out var parsedContentLength)
                ? parsedContentLength
                : 0;

            var bodyCharacters = new char[contentLength];
            var totalRead = 0;
            while (totalRead < contentLength)
            {
                var read = await reader.ReadAsync(bodyCharacters.AsMemory(totalRead, contentLength - totalRead), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            var requestLineParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            requests.Add(
                new(
                    requestLineParts.ElementAtOrDefault(0) ?? string.Empty,
                    requestLineParts.ElementAtOrDefault(1) ?? string.Empty,
                    headers,
                    new string(bodyCharacters, 0, totalRead)));

            var statusCode = statusCodeSelector(attempt);
            var reasonPhrase = statusCode == 500 ? "Server Error" : statusCode == 201 ? "Created" : "OK";
            var responseBody = responseBodySelector?.Invoke(attempt)
                ?? $$"""{"payload":{"id":"42","trace":"trace-123"},"attempt":{{attempt}}}""";
            var response = $"HTTP/1.1 {statusCode} {reasonPhrase}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\nConnection: close\r\n\r\n{responseBody}";
            var responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        return requests;
    }

    private static async Task<List<CapturedRequest>> WaitForRequestsAsync(Task<List<CapturedRequest>> requestCaptureTask, int timeoutMilliseconds = 5000)
    {
        Task completedTask = await Task.WhenAny(requestCaptureTask, Task.Delay(timeoutMilliseconds));
        if (!ReferenceEquals(completedTask, requestCaptureTask))
        {
            Assert.Fail($"Timed out waiting for captured requests after {timeoutMilliseconds} ms.");
        }

        return await requestCaptureTask;
    }

    private sealed record CapturedRequest(
        string Method,
        string PathAndQuery,
        Dictionary<string, string> Headers,
        string Body);

    private sealed class RecordingExecutionHistoryRepository : IExecutionHistoryRepository
    {
        public Task<List<ExecutionRun>> Load(Guid workspaceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<ExecutionRun>());
        }

        public Task Add(ExecutionRun run, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
