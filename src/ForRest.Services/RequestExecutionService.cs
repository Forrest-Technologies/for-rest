namespace ForRest.Services;

public sealed class RequestExecutionService(
    RequestCompiler requestCompiler,
    ResponseExtractionService responseExtractionService,
    IExecutionHistoryRepository executionHistoryRepository,
    IRepeatRunnerService repeatRunnerService,
    IScriptEngine scriptEngine,
    ILogger<RequestExecutionService> logger) : IRequestExecutionService
{
    #region Public Methods

    public async Task<RequestExecutionResult> Execute(
        AppProfile profile,
        WorkspaceSnapshot workspace,
        RequestDefinition request,
        EnvironmentDefinition? environment,
        CancellationToken cancellationToken = default)
    {
        return await Execute(profile, workspace, request, environment, [], cancellationToken);
    }

    public async Task<RequestExecutionResult> Execute(
        AppProfile profile,
        WorkspaceSnapshot workspace,
        RequestDefinition request,
        EnvironmentDefinition? environment,
        IReadOnlyList<VariableDefinition> initialRuntimeVariables,
        CancellationToken cancellationToken = default)
    {
        var runtimeVariables = initialRuntimeVariables
            .Where(static item => item.Scope == VariableScope.Runtime)
            .Select(
                static item => item with
                {
                    Scope = VariableScope.Runtime,
                })
            .ToList();
        var consoleEntries = new List<ConsoleEntry>();
        var testResults = new List<TestResult>();

        async Task<ExecutionRun> ExecuteIteration(int iteration, CancellationToken iterationCancellationToken)
        {
            var startedUtc = DateTimeOffset.UtcNow;
            var compileResult = requestCompiler.Prepare(
                request,
                BuildSystemVariables(),
                profile.GlobalVariables,
                workspace.Workspace.Variables,
                environment?.Variables ?? [],
                runtimeVariables);

            if (!compileResult.Succeeded || compileResult.Value is null)
            {
                return new()
                {
                    WorkspaceId = workspace.Workspace.Id,
                    RequestId = request.Id,
                    RequestName = request.Name,
                    Iteration = iteration,
                    StartedUtc = startedUtc,
                    CompletedUtc = DateTimeOffset.UtcNow,
                    State = ExecutionState.Failed,
                    ErrorMessage = compileResult.ErrorMessage ?? "Request compilation failed.",
                };
            }

            var preparedRequest = compileResult.Value;

            async Task<ResponseSnapshot?> ExecuteScriptSend(PreparedRequest scriptedRequest)
            {
                using var scriptedHandler = new HttpClientHandler
                {
                    AllowAutoRedirect = scriptedRequest.FollowRedirects,
                    ServerCertificateCustomValidationCallback = scriptedRequest.ValidateSsl
                        ? DefaultCertificateValidation
                        : HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                };
                using var scriptedClient = new HttpClient(scriptedHandler)
                {
                    Timeout = TimeSpan.FromMilliseconds(Math.Max(1, scriptedRequest.TimeoutMilliseconds)),
                };

                var (scriptedResponse, scriptedDuration, scriptedError) = await SendWithRetry(
                    scriptedClient,
                    scriptedRequest,
                    request,
                    iterationCancellationToken);

                if (!string.IsNullOrWhiteSpace(scriptedError))
                {
                    throw new InvalidOperationException(scriptedError);
                }

                if (scriptedResponse is null)
                {
                    return null;
                }

                using (scriptedResponse)
                {
                    return await BuildResponseSnapshot(scriptedResponse, scriptedDuration, iterationCancellationToken);
                }
            }

            var preRequestResult = await scriptEngine.Run(
                new()
                {
                    Script = request.PreRequestScript,
                    PreparedRequest = preparedRequest,
                    Workspace = workspace.Workspace,
                    GlobalVariables = profile.GlobalVariables,
                    WorkspaceVariables = workspace.Workspace.Variables,
                    EnvironmentVariables = environment?.Variables ?? [],
                    RequestVariables = request.Variables,
                    RuntimeVariables = runtimeVariables,
                    SendAsync = ExecuteScriptSend,
                    MaxSendIterations = request.MaxSendIterations,
                },
                iterationCancellationToken);

            consoleEntries.AddRange(preRequestResult.ConsoleEntries);
            if (!string.IsNullOrWhiteSpace(preRequestResult.ErrorMessage))
            {
                return new()
                {
                    WorkspaceId = workspace.Workspace.Id,
                    RequestId = request.Id,
                    RequestName = request.Name,
                    Iteration = iteration,
                    StartedUtc = startedUtc,
                    CompletedUtc = DateTimeOffset.UtcNow,
                    State = ExecutionState.Failed,
                    ErrorMessage = preRequestResult.ErrorMessage,
                    ConsoleEntries = [.. preRequestResult.ConsoleEntries],
                };
            }

            runtimeVariables = MergeRuntimeVariables(runtimeVariables, preRequestResult.RuntimeVariables);
            preparedRequest = preRequestResult.PreparedRequest;

            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = preparedRequest.FollowRedirects,
                ServerCertificateCustomValidationCallback = preparedRequest.ValidateSsl
                    ? DefaultCertificateValidation
                    : HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(Math.Max(1, preparedRequest.TimeoutMilliseconds)),
            };
            HttpResponseMessage? httpResponse = null;
            string errorMessage = string.Empty;
            long durationMilliseconds = 0;

            (httpResponse, durationMilliseconds, errorMessage) = await SendWithRetry(client, preparedRequest, request, iterationCancellationToken);
            ResponseSnapshot? responseSnapshot;
            if (httpResponse is null)
            {
                responseSnapshot = null;
            }
            else
            {
                using (httpResponse)
                {
                    responseSnapshot = await BuildResponseSnapshot(httpResponse, durationMilliseconds, iterationCancellationToken);
                }
            }

            var extractedVariables = responseExtractionService.Extract(responseSnapshot, request.Extractions);
            runtimeVariables = MergeRuntimeVariables(runtimeVariables, extractedVariables);

            var testScriptResult = await scriptEngine.Run(
                new()
                {
                    Script = request.TestsScript,
                    PreparedRequest = preparedRequest,
                    Response = responseSnapshot,
                    Workspace = workspace.Workspace,
                    GlobalVariables = profile.GlobalVariables,
                    WorkspaceVariables = workspace.Workspace.Variables,
                    EnvironmentVariables = environment?.Variables ?? [],
                    RequestVariables = request.Variables,
                    RuntimeVariables = runtimeVariables,
                    SendAsync = ExecuteScriptSend,
                    MaxSendIterations = request.MaxSendIterations,
                },
                iterationCancellationToken);

            responseSnapshot = testScriptResult.Response ?? responseSnapshot;
            runtimeVariables = MergeRuntimeVariables(runtimeVariables, testScriptResult.RuntimeVariables);
            consoleEntries.AddRange(testScriptResult.ConsoleEntries);
            testResults.AddRange(testScriptResult.Tests);

            var run = new ExecutionRun
            {
                WorkspaceId = workspace.Workspace.Id,
                RequestId = request.Id,
                RequestName = request.Name,
                Iteration = iteration,
                StartedUtc = startedUtc,
                CompletedUtc = DateTimeOffset.UtcNow,
                State = string.IsNullOrWhiteSpace(errorMessage) && string.IsNullOrWhiteSpace(testScriptResult.ErrorMessage)
                    ? ExecutionState.Completed
                    : ExecutionState.Failed,
                ErrorMessage = string.Join(
                    Environment.NewLine,
                    new[] { errorMessage, testScriptResult.ErrorMessage }.Where(static item => !string.IsNullOrWhiteSpace(item))),
                TargetUri = preparedRequest.Uri.ToString(),
                RawRequest = preparedRequest.RawRequest,
                Response = responseSnapshot,
                ConsoleEntries =
                [
                    .. preRequestResult.ConsoleEntries,
                    .. testScriptResult.ConsoleEntries,
                ],
                Tests =
                [
                    .. testScriptResult.Tests,
                ],
                RuntimeVariables = [.. runtimeVariables],
            };

            if (request.SaveResponseToHistory)
            {
                await executionHistoryRepository.Add(run, iterationCancellationToken);
            }

            return run;
        }

        var runs = await repeatRunnerService.Run(request.Schedule, ExecuteIteration, cancellationToken);
        var latestRun = runs.LastOrDefault();

        return new()
        {
            State = latestRun?.State ?? ExecutionState.Completed,
            Runs = runs,
            LatestResponse = latestRun?.Response,
            RuntimeVariables = [.. runtimeVariables],
            ConsoleEntries = [.. consoleEntries],
            Tests = [.. testResults],
        };
    }

    #endregion

    #region Private Methods

    private static bool DefaultCertificateValidation(
        HttpRequestMessage requestMessage,
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        return sslPolicyErrors == SslPolicyErrors.None;
    }

    private static HttpRequestMessage BuildHttpRequest(PreparedRequest preparedRequest)
    {
        var method = new HttpMethod(preparedRequest.Method.ToString().ToUpperInvariant());
        var request = new HttpRequestMessage(method, preparedRequest.Uri);

        foreach (var header in preparedRequest.Headers.Where(static item => item.IsEnabled))
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Content ??= new ByteArrayContent([]);
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        request.Content = preparedRequest.Body.Mode switch
        {
            RequestBodyMode.None => null,
            RequestBodyMode.FormUrlEncoded => new FormUrlEncodedContent(
                preparedRequest.Body.FormValues
                    .Where(static item => item.IsEnabled)
                    .Select(static item => KeyValuePair.Create(item.Key, item.Value))),
            RequestBodyMode.MultipartFormData => BuildMultipartContent(preparedRequest.Body),
            RequestBodyMode.Json => BuildStringContent(preparedRequest.Body.RawContent, preparedRequest.Body.ContentType, "application/json"),
            _ => BuildStringContent(preparedRequest.Body.RawContent, preparedRequest.Body.ContentType, "text/plain"),
        };

        return request;
    }

    private static MultipartFormDataContent BuildMultipartContent(RequestBodyDefinition body)
    {
        var content = new MultipartFormDataContent();
        foreach (var field in body.FormValues.Where(static item => item.IsEnabled))
        {
            content.Add(new StringContent(field.Value), field.Key);
        }

        return content;
    }

    private static StringContent BuildStringContent(string body, string contentType, string defaultContentType)
    {
        var stringContent = new StringContent(body ?? string.Empty, Encoding.UTF8);
        stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(contentType) ? defaultContentType : contentType);
        return stringContent;
    }

    private static async Task<ResponseSnapshot> BuildResponseSnapshot(HttpResponseMessage response, long durationMilliseconds, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .SelectMany(
                header => header.Value.Select(
                    value => new KeyValueDefinition
                    {
                        Key = header.Key,
                        Value = value,
                    }))
            .ToList();

        var cookies = response.Headers.TryGetValues("Set-Cookie", out var cookieValues)
            ? cookieValues.Select(static cookie => new KeyValueDefinition
            {
                Key = "Set-Cookie",
                Value = cookie,
            }).ToList()
            : [];

        return new()
        {
            StatusCode = (int)response.StatusCode,
            ReasonPhrase = response.ReasonPhrase ?? string.Empty,
            ContentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty,
            SizeBytes = Encoding.UTF8.GetByteCount(body),
            DurationMilliseconds = durationMilliseconds,
            Body = body,
            RawResponse = BuildRawResponse(response, body),
            Headers = headers,
            Cookies = cookies,
            ReceivedUtc = DateTimeOffset.UtcNow,
        };
    }

    private static List<VariableDefinition> BuildSystemVariables()
    {
        return
        [
            .. Environment.GetEnvironmentVariables()
                .Cast<DictionaryEntry>()
                .Where(static item => item.Key is not null && item.Value is not null)
                .Select(
                    static item => new VariableDefinition
                    {
                        Key = item.Key.ToString() ?? string.Empty,
                        Value = item.Value?.ToString() ?? string.Empty,
                        Scope = VariableScope.System,
                    }),
        ];
    }

    private static string BuildRawResponse(HttpResponseMessage response, string body)
    {
        var builder = new StringBuilder();
        builder.Append("HTTP/");
        builder.Append(response.Version);
        builder.Append(' ');
        builder.Append((int)response.StatusCode);
        builder.Append(' ');
        builder.AppendLine(response.ReasonPhrase);

        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            foreach (var value in header.Value)
            {
                builder.Append(header.Key);
                builder.Append(": ");
                builder.AppendLine(value);
            }
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.AppendLine();
            builder.Append(body);
        }

        return builder.ToString().TrimEnd();
    }

    private static List<VariableDefinition> MergeRuntimeVariables(
        IEnumerable<VariableDefinition> existingVariables,
        IEnumerable<VariableDefinition> incomingVariables)
    {
        var merged = existingVariables
            .Where(static item => item.Scope == VariableScope.Runtime)
            .ToDictionary(static item => item.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var variable in incomingVariables.Where(static item => item.Scope == VariableScope.Runtime))
        {
            merged[variable.Key] = variable with
            {
                Scope = VariableScope.Runtime,
            };
        }

        return merged.Values.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<(HttpResponseMessage? Response, long DurationMilliseconds, string ErrorMessage)> SendWithRetry(
        HttpClient client,
        PreparedRequest preparedRequest,
        RequestDefinition request,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, request.Retry.Count + 1);
        HttpResponseMessage? response = null;
        string errorMessage = string.Empty;
        long durationMilliseconds = 0;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var attemptRequest = BuildHttpRequest(preparedRequest);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                response = await client.SendAsync(attemptRequest, cancellationToken);
                stopwatch.Stop();
                durationMilliseconds = stopwatch.ElapsedMilliseconds;

                if (!ShouldRetry(response, attempt, attempts))
                {
                    return (response, durationMilliseconds, string.Empty);
                }

                response.Dispose();
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                durationMilliseconds = stopwatch.ElapsedMilliseconds;
                errorMessage = exception.Message;
                logger.LogWarning(exception, "Request execution failed for {RequestName} on attempt {Attempt}", request.Name, attempt);

                if (attempt >= attempts)
                {
                    return (null, durationMilliseconds, errorMessage);
                }
            }

            if (attempt < attempts && request.Retry.IntervalMilliseconds > 0)
            {
                await Task.Delay(request.Retry.IntervalMilliseconds, cancellationToken);
            }
        }

        return (response, durationMilliseconds, errorMessage);
    }

    private static bool ShouldRetry(HttpResponseMessage response, int attempt, int maxAttempts)
    {
        return attempt < maxAttempts && (int)response.StatusCode >= 500;
    }

    #endregion
}
