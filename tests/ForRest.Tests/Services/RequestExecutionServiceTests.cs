using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ForRest.Tests.Services;

[TestClass]
public sealed class RequestExecutionServiceTests
{
    #region Public Methods

    [TestMethod]
    public async Task Execute_sends_method_headers_and_body_and_captures_response()
    {
        var workspaceId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var historyRepository = new RecordingExecutionHistoryRepository();
        var requestExecutionService = new RequestExecutionService(
            new RequestCompiler(new VariableResolver()),
            new ResponseExtractionService(),
            historyRepository,
            new RepeatRunnerService(),
            new NoOpScriptEngine(),
            NullLogger<RequestExecutionService>.Instance);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = CaptureSingleRequest(listener);

        try
        {
            var request = new RequestDefinition
            {
                Id = requestId,
                WorkspaceId = workspaceId,
                Name = "Loopback Create",
                Method = HttpMethodKind.Post,
                UrlTemplate = $"http://127.0.0.1:{port}/echo",
                Headers =
                [
                    new()
                    {
                        Key = "Accept",
                        Value = "application/json",
                    },
                    new()
                    {
                        Key = "X-Test-Mode",
                        Value = "loopback",
                    },
                ],
                Body = new()
                {
                    Mode = RequestBodyMode.Json,
                    ContentType = "application/json",
                    RawContent = """{"message":"from ForRest"}""",
                },
                SaveResponseToHistory = true,
            };

            var workspace = new WorkspaceSnapshot
            {
                Workspace = new()
                {
                    Id = workspaceId,
                    Name = "Loopback Workspace",
                },
            };

            var result = await requestExecutionService.Execute(new(), workspace, request, null);
            var capturedRequest = await serverTask;

            Assert.AreEqual("POST", capturedRequest.Method);
            Assert.AreEqual("/echo", capturedRequest.PathAndQuery);
            Assert.AreEqual("loopback", capturedRequest.Headers["X-Test-Mode"]);
            Assert.AreEqual("application/json", capturedRequest.Headers["Accept"]);
            StringAssert.StartsWith(capturedRequest.Headers["Content-Type"], "application/json");
            Assert.AreEqual("""{"message":"from ForRest"}""", capturedRequest.Body);

            Assert.AreEqual(ExecutionState.Completed, result.State);
            Assert.IsNotNull(result.LatestResponse);
            Assert.AreEqual(201, result.LatestResponse.StatusCode);
            Assert.AreEqual("Created", result.LatestResponse.ReasonPhrase);
            Assert.AreEqual("application/json", result.LatestResponse.ContentType);
            StringAssert.Contains(result.LatestResponse.Body, "\"received\":true");
            Assert.HasCount(1, result.Runs.Single().Responses);
            StringAssert.Contains(result.Runs.Single().RawRequest, "X-Test-Mode: loopback");
            StringAssert.Contains(result.Runs.Single().RawRequest, "POST http://127.0.0.1:");
            Assert.HasCount(1, historyRepository.Runs);
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_enforces_max_send_iterations_across_pre_request_and_tests_scripts()
    {
        var workspaceId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var historyRepository = new RecordingExecutionHistoryRepository();
        var requestExecutionService = CreateScriptEnabledService(historyRepository);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = CaptureRequests(listener, 1, static _ => 200);

        try
        {
            var request = new RequestDefinition
            {
                Id = requestId,
                WorkspaceId = workspaceId,
                Name = "Budgeted Send",
                Method = HttpMethodKind.Get,
                UrlTemplate = $"http://127.0.0.1:{port}/budget",
                SaveResponseToHistory = false,
                MaxSendIterations = 1,
                PreRequestScript = "await request.send();",
                TestsScript = "await request.send();",
            };

            var workspace = new WorkspaceSnapshot
            {
                Workspace = new()
                {
                    Id = workspaceId,
                    Name = "Budget Workspace",
                },
            };

            var result = await requestExecutionService.Execute(new(), workspace, request, null);
            var capturedRequests = await serverTask;

            Assert.AreEqual(ExecutionState.Failed, result.State);
            Assert.HasCount(1, capturedRequests);
            StringAssert.Contains(result.Runs.Single().ErrorMessage, "disabled");
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_reextracts_runtime_variables_after_tests_script_send()
    {
        var workspaceId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var historyRepository = new RecordingExecutionHistoryRepository();
        var requestExecutionService = CreateScriptEnabledService(historyRepository);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = CaptureRequests(listener, 2, static _ => 200);

        try
        {
            var request = new RequestDefinition
            {
                Id = requestId,
                WorkspaceId = workspaceId,
                Name = "Reextract Send",
                Method = HttpMethodKind.Get,
                UrlTemplate = $"http://127.0.0.1:{port}/extract?step=1",
                SaveResponseToHistory = true,
                MaxSendIterations = 2,
                PreRequestScript = "await request.send();",
                TestsScript =
                """
                request.Url = request.Url.Replace("step=1", "step=2", StringComparison.Ordinal);
                await request.send();
                """,
                Extractions =
                [
                    new()
                    {
                        Name = "attempt",
                        Selector = "$.attempt",
                        TargetVariableName = "attempt",
                        TargetScope = VariableScope.Runtime,
                    },
                ],
            };

            var workspace = new WorkspaceSnapshot
            {
                Workspace = new()
                {
                    Id = workspaceId,
                    Name = "Extract Workspace",
                },
            };

            var result = await requestExecutionService.Execute(new(), workspace, request, null);
            var capturedRequests = await serverTask;
            var run = result.Runs.Single();

            Assert.AreEqual(ExecutionState.Completed, result.State);
            Assert.HasCount(2, capturedRequests);
            Assert.AreEqual("/extract?step=1", capturedRequests[0].PathAndQuery);
            Assert.AreEqual("/extract?step=2", capturedRequests[1].PathAndQuery);
            Assert.AreEqual("2", result.RuntimeVariables.Single(static item => item.Key == "attempt").Value);
            Assert.AreEqual($"http://127.0.0.1:{port}/extract?step=2", run.TargetUri);
            Assert.HasCount(2, run.Responses);
            StringAssert.Contains(run.Responses[0].Body, "\"attempt\":1");
            StringAssert.Contains(run.Responses[1].Body, "\"attempt\":2");
            StringAssert.Contains(run.Response?.Body ?? string.Empty, "\"attempt\":2");
            Assert.AreEqual("2", historyRepository.Runs.Single().RuntimeVariables.Single(static item => item.Key == "attempt").Value);
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_workspace_execute_runs_nested_request_without_nested_history_entries()
    {
        var workspaceId = Guid.NewGuid();
        var historyRepository = new RecordingExecutionHistoryRepository();
        var requestExecutionService = CreateScriptEnabledService(historyRepository);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = CaptureRequests(
            listener,
            [
                new CapturedResponse(200, """{"token":"abc123"}"""),
                new CapturedResponse(200, """{"secured":true}"""),
            ]);

        try
        {
            var helperRequest = new RequestDefinition
            {
                WorkspaceId = workspaceId,
                Name = "Token Helper",
                Method = HttpMethodKind.Get,
                UrlTemplate = $"http://127.0.0.1:{port}/token",
                SaveResponseToHistory = true,
                Extractions =
                [
                    new()
                    {
                        Name = "auth_token",
                        Selector = "$.token",
                        TargetVariableName = "auth_token",
                        TargetScope = VariableScope.Runtime,
                    },
                ],
            };

            var parentRequest = new RequestDefinition
            {
                WorkspaceId = workspaceId,
                Name = "Secure Data",
                Method = HttpMethodKind.Get,
                UrlTemplate = $"http://127.0.0.1:{port}/secure",
                SaveResponseToHistory = true,
                MaxSendIterations = 1,
                PreRequestScript =
                """
                var auth = await workspace.execute("/requests/auth/token");
                request.SetHeader("Authorization", $"Bearer {auth.token}");
                request.SetHeader("X-Token-Var", variables.Get("auth_token"));
                """,
            };

            var workspace = new WorkspaceSnapshot
            {
                Workspace = new()
                {
                    Id = workspaceId,
                    Name = "Secure Workspace",
                },
                Nodes =
                [
                    new()
                    {
                        WorkspaceId = workspaceId,
                        Kind = WorkspaceNodeKind.Request,
                        Name = "Secure Data",
                        Location = "/requests/secure/data",
                        SortOrder = 0,
                        Request = parentRequest,
                    },
                    new()
                    {
                        WorkspaceId = workspaceId,
                        Kind = WorkspaceNodeKind.Request,
                        Name = "Token Helper",
                        Location = "/requests/auth/token",
                        SortOrder = 1,
                        Request = helperRequest,
                    },
                ],
            };

            var result = await requestExecutionService.Execute(new(), workspace, parentRequest, null);
            var capturedRequests = await serverTask;

            Assert.AreEqual(ExecutionState.Completed, result.State, result.Runs.Single().ErrorMessage);
            Assert.HasCount(2, capturedRequests);
            Assert.AreEqual("/token", capturedRequests[0].PathAndQuery);
            Assert.AreEqual("/secure", capturedRequests[1].PathAndQuery);
            Assert.AreEqual("Bearer abc123", capturedRequests[1].Headers["Authorization"]);
            Assert.AreEqual("abc123", capturedRequests[1].Headers["X-Token-Var"]);
            Assert.AreEqual("abc123", result.RuntimeVariables.Single(static item => item.Key == "auth_token").Value);
            Assert.HasCount(1, historyRepository.Runs);
            Assert.AreEqual("Secure Data", historyRepository.Runs.Single().RequestName);
        }
        finally
        {
            listener.Stop();
        }
    }

    #endregion

    #region Private Methods

    private static IRequestExecutionService CreateScriptEnabledService(RecordingExecutionHistoryRepository historyRepository)
    {
        return new RequestExecutionService(
            new RequestCompiler(new VariableResolver()),
            new ResponseExtractionService(),
            historyRepository,
            new RepeatRunnerService(),
            new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance),
            NullLogger<RequestExecutionService>.Instance);
    }

    private static async Task<CapturedRequest> CaptureSingleRequest(TcpListener listener, CancellationToken cancellationToken = default)
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

        var body = new string(bodyCharacters, 0, totalRead);
        var responseBody = """{"received":true,"source":"loopback"}""";
        var response = $"HTTP/1.1 201 Created\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\nSet-Cookie: session=loopback; Path=/\r\nConnection: close\r\n\r\n{responseBody}";
        var responseBytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(responseBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var requestLineParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new(
            requestLineParts.ElementAtOrDefault(0) ?? string.Empty,
            requestLineParts.ElementAtOrDefault(1) ?? string.Empty,
            headers,
            body);
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
            requests.Add(await CaptureSingleRequest(listener, statusCodeSelector(attempt), attempt, cancellationToken));
        }

        return requests;
    }

    private static async Task<List<CapturedRequest>> CaptureRequests(
        TcpListener listener,
        IReadOnlyList<CapturedResponse> responses,
        CancellationToken cancellationToken = default)
    {
        var requests = new List<CapturedRequest>();

        for (var attempt = 0; attempt < responses.Count; attempt++)
        {
            requests.Add(await CaptureSingleRequest(listener, responses[attempt], attempt + 1, cancellationToken));
        }

        return requests;
    }

    private static async Task<CapturedRequest> CaptureSingleRequest(
        TcpListener listener,
        int statusCode,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        return await CaptureSingleRequest(
            listener,
            new CapturedResponse(statusCode, null),
            attempt,
            cancellationToken);
    }

    private static async Task<CapturedRequest> CaptureSingleRequest(
        TcpListener listener,
        CapturedResponse responseDefinition,
        int attempt,
        CancellationToken cancellationToken = default)
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

        var body = new string(bodyCharacters, 0, totalRead);
        var statusCode = responseDefinition.StatusCode;
        var reasonPhrase = responseDefinition.ReasonPhrase ?? (statusCode == 201 ? "Created" : "OK");
        var responseBody = responseDefinition.Body ?? $$"""{"received":true,"source":"loopback","attempt":{{attempt}}}""";
        var contentType = string.IsNullOrWhiteSpace(responseDefinition.ContentType)
            ? "application/json"
            : responseDefinition.ContentType;
        var response = $"HTTP/1.1 {statusCode} {reasonPhrase}\r\nContent-Type: {contentType}\r\nContent-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\nSet-Cookie: session=loopback; Path=/\r\nConnection: close\r\n\r\n{responseBody}";
        var responseBytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(responseBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var requestLineParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new(
            requestLineParts.ElementAtOrDefault(0) ?? string.Empty,
            requestLineParts.ElementAtOrDefault(1) ?? string.Empty,
            headers,
            body);
    }

    #endregion

    #region Private Types

    private sealed record CapturedRequest(
        string Method,
        string PathAndQuery,
        Dictionary<string, string> Headers,
        string Body);

    private sealed record CapturedResponse(
        int StatusCode,
        string? Body,
        string? ReasonPhrase = null,
        string? ContentType = "application/json");

    private sealed class RecordingExecutionHistoryRepository : IExecutionHistoryRepository
    {
        public List<ExecutionRun> Runs { get; } = [];

        public Task<List<ExecutionRun>> Load(Guid workspaceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Runs.Where(item => item.WorkspaceId == workspaceId).ToList());
        }

        public Task Add(ExecutionRun run, CancellationToken cancellationToken = default)
        {
            Runs.Add(run);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpScriptEngine : IScriptEngine
    {
        public ScriptValidationResult Validate(string script)
        {
            return new();
        }

        public Task<ScriptExecutionResult> Run(ScriptExecutionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScriptExecutionResult
            {
                PreparedRequest = request.PreparedRequest,
                RuntimeVariables = request.RuntimeVariables,
            });
        }
    }

    #endregion
}
