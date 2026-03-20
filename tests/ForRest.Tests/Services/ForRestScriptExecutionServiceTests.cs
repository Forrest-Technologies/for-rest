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
                meta {
                  name = "Scripted Echo"
                }

                vars {
                  request resource_id = "42"
                  runtime trace_id = "trace-123"
                }

                request {
                  method = POST
                  url = "http://127.0.0.1:__PORT__/echo/{{resource_id}}?trace={{trace_id}}"
                  content_type = "application/json"
                  history = false
                }

                headers {
                  Accept = "application/json"
                  X-Trace-Id = "{{trace_id}}"
                }

                body json """
                {
                  "id": "{{resource_id}}",
                  "trace": "{{trace_id}}"
                }
                """

                extract {
                  runtime echoed_id = json "$.payload.id"
                }

                tests {
                  status == 201 "returns 201"
                  json "$.payload.trace" == "trace-123" "trace matches"
                }
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

            Assert.IsTrue(result.Compilation.Succeeded);
            Assert.IsNotNull(result.Execution);
            Assert.AreEqual(ExecutionState.Completed, result.Execution.State);
            Assert.AreEqual("/echo/42?trace=trace-123", capturedRequest.PathAndQuery);
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
        var requestCaptureTask = CaptureRequests(listener, 2, static _ => 200);

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
            Assert.HasCount(2, capturedRequests);
            Assert.AreEqual("1", result.Execution.RuntimeVariables.Single(static item => item.Key == "probe_attempt").Value);
            StringAssert.Contains(result.Execution.LatestResponse?.Body ?? string.Empty, "\"attempt\":2");
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
            var responseBody = $$"""{"payload":{"id":"42","trace":"trace-123"},"attempt":{{attempt}}}""";
            var response = $"HTTP/1.1 {statusCode} {reasonPhrase}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\nConnection: close\r\n\r\n{responseBody}";
            var responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        return requests;
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
