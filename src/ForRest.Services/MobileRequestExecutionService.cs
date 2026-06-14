using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ForRest.Services;

public sealed class MobileRequestExecutionService(
	RequestCompiler requestCompiler,
	ResponseExtractionService responseExtractionService,
	IExecutionHistoryRepository executionHistoryRepository,
	IRepeatRunnerService repeatRunnerService,
	ILogger<MobileRequestExecutionService> logger) : IRequestExecutionService
{
	private const string ScriptingWarningMessage = "Android build runs requests in mobile-safe mode. Advanced flow scripts and test assertions are skipped.";

	public Task<RequestExecutionResult> Execute(
		AppProfile profile,
		WorkspaceSnapshot workspace,
		RequestDefinition request,
		EnvironmentDefinition? environment,
		CancellationToken cancellationToken = default)
	{
		return Execute(profile, workspace, request, environment, [], cancellationToken);
	}

	public async Task<RequestExecutionResult> Execute(
		AppProfile profile,
		WorkspaceSnapshot workspace,
		RequestDefinition request,
		EnvironmentDefinition? environment,
		IReadOnlyList<VariableDefinition> initialRuntimeVariables,
		CancellationToken cancellationToken = default)
	{
		List<VariableDefinition> runtimeVariables =
		[
			.. initialRuntimeVariables
				.Where(static item => item.Scope == VariableScope.Runtime)
				.Select(
					static item => item with
					{
						Scope = VariableScope.Runtime,
					}),
		];
		List<ConsoleEntry> consoleEntries = [];

		async Task<ExecutionRun> ExecuteIteration(int iteration, CancellationToken iterationCancellationToken)
		{
			DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
			OperationResult<PreparedRequest> compileResult = requestCompiler.Prepare(
				request,
				BuildSystemVariables(),
				profile.GlobalVariables,
				workspace.Workspace.Variables,
				environment?.Variables ?? [],
				runtimeVariables);
			List<ConsoleEntry> iterationConsoleEntries = BuildMobileSafeConsoleEntries(request);

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
					ConsoleEntries = [.. iterationConsoleEntries],
					RuntimeVariables = [.. runtimeVariables],
				};
			}

			PreparedRequest preparedRequest = compileResult.Value;
			using HttpClientHandler handler = new()
			{
				AllowAutoRedirect = preparedRequest.FollowRedirects,
				ServerCertificateCustomValidationCallback = preparedRequest.ValidateSsl
					? DefaultCertificateValidation
					: HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
			};
			using HttpClient client = new(handler)
			{
				Timeout = TimeSpan.FromMilliseconds(Math.Max(1, preparedRequest.TimeoutMilliseconds)),
			};

			(HttpResponseMessage? response, long durationMilliseconds, string errorMessage) = await SendWithRetry(
				client,
				preparedRequest,
				request,
				iterationCancellationToken);
			ResponseSnapshot? responseSnapshot = null;

			if (response is not null)
			{
				using (response)
				{
					responseSnapshot = await BuildResponseSnapshot(response, durationMilliseconds, iterationCancellationToken);
				}

				List<VariableDefinition> extractedVariables = responseExtractionService.Extract(responseSnapshot, request.Extractions);
				runtimeVariables = MergeRuntimeVariables(runtimeVariables, extractedVariables);
			}

			ExecutionRun run = new()
			{
				WorkspaceId = workspace.Workspace.Id,
				RequestId = request.Id,
				RequestName = request.Name,
				Iteration = iteration,
				StartedUtc = startedUtc,
				CompletedUtc = DateTimeOffset.UtcNow,
				State = string.IsNullOrWhiteSpace(errorMessage) ? ExecutionState.Completed : ExecutionState.Failed,
				ErrorMessage = errorMessage,
				TargetUri = preparedRequest.Uri.ToString(),
				RawRequest = preparedRequest.RawRequest,
				Response = responseSnapshot,
				Responses = responseSnapshot is null ? [] : [responseSnapshot],
				Requests = responseSnapshot is null ? [] : [PreparedRequestSnapshotBuilder.Build(preparedRequest)],
				ConsoleEntries = [.. iterationConsoleEntries],
				RuntimeVariables = [.. runtimeVariables],
			};

			if (request.SaveResponseToHistory)
			{
				await executionHistoryRepository.Add(run, iterationCancellationToken);
			}

			consoleEntries.AddRange(iterationConsoleEntries);
			return run;
		}

		List<ExecutionRun> runs = await repeatRunnerService.Run(request.Schedule, ExecuteIteration, cancellationToken);
		ExecutionRun? latestRun = runs.LastOrDefault();

		return new()
		{
			State = latestRun?.State ?? ExecutionState.Completed,
			Runs = runs,
			LatestResponse = latestRun?.Response,
			RuntimeVariables = [.. runtimeVariables],
			ConsoleEntries = [.. consoleEntries],
			Tests = [],
		};
	}

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
		HttpMethod method = new(preparedRequest.Method.ToString().ToUpperInvariant());
		HttpRequestMessage request = new(method, preparedRequest.Uri);

		// Assign the body before headers so content headers (Content-Type, Content-Disposition, ...)
		// land on the final content instead of a placeholder that the body assignment would replace.
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

		foreach (KeyValueDefinition header in preparedRequest.Headers.Where(static item => item.IsEnabled))
		{
			if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
			{
				request.Content ??= new ByteArrayContent([]);
				request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
			}
		}

		return request;
	}

	private static MultipartFormDataContent BuildMultipartContent(RequestBodyDefinition body)
	{
		MultipartFormDataContent content = new();
		foreach (KeyValueDefinition field in body.FormValues.Where(static item => item.IsEnabled))
		{
			content.Add(new StringContent(field.Value), field.Key);
		}

		return content;
	}

	private static StringContent BuildStringContent(string body, string contentType, string defaultContentType)
	{
		StringContent stringContent = new(body ?? string.Empty, Encoding.UTF8);
		string mediaType = string.IsNullOrWhiteSpace(contentType) ? defaultContentType : contentType;
		try
		{
			stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
		}
		catch (FormatException)
		{
			throw new InvalidOperationException($"The request content type '{mediaType}' is not a valid media type.");
		}

		return stringContent;
	}

	private static async Task<ResponseSnapshot> BuildResponseSnapshot(
		HttpResponseMessage response,
		long durationMilliseconds,
		CancellationToken cancellationToken)
	{
		string body = await response.Content.ReadAsStringAsync(cancellationToken);
		List<KeyValueDefinition> headers = response.Headers
			.Concat(response.Content.Headers)
			.SelectMany(
				header => header.Value.Select(
					value => new KeyValueDefinition
					{
						Key = header.Key,
						Value = value,
					}))
			.ToList();

		List<KeyValueDefinition> cookies = response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookieValues)
			? cookieValues.Select(
				static cookie => new KeyValueDefinition
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
		StringBuilder builder = new();
		builder.Append("HTTP/");
		builder.Append(response.Version);
		builder.Append(' ');
		builder.Append((int)response.StatusCode);
		builder.Append(' ');
		builder.AppendLine(response.ReasonPhrase);

		foreach (var header in response.Headers.Concat(response.Content.Headers))
		{
			foreach (string value in header.Value)
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
		Dictionary<string, VariableDefinition> merged = existingVariables
			.Where(static item => item.Scope == VariableScope.Runtime)
			.ToDictionary(static item => item.Key, StringComparer.OrdinalIgnoreCase);

		foreach (VariableDefinition variable in incomingVariables.Where(static item => item.Scope == VariableScope.Runtime))
		{
			// Preserve secret status across the merge so a script-assigned value that lost its flag
			// cannot downgrade an existing secret runtime variable into plaintext on persistence.
			bool resolvedIsSecret = variable.IsSecret
				|| (merged.TryGetValue(variable.Key, out VariableDefinition? existing) && existing.IsSecret);

			merged[variable.Key] = variable with
			{
				Scope = VariableScope.Runtime,
				IsSecret = resolvedIsSecret,
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
		int attempts = Math.Max(1, request.Retry.Count + 1);
		HttpResponseMessage? response = null;
		string errorMessage = string.Empty;
		long durationMilliseconds = 0;

		for (int attempt = 1; attempt <= attempts; attempt++)
		{
			HttpRequestMessage attemptRequest;
			try
			{
				attemptRequest = BuildHttpRequest(preparedRequest);
			}
			catch (Exception exception)
			{
				// Construction failures (e.g. a malformed content type) are not transient; fail the
				// run with the reason instead of letting the exception escape the pipeline.
				logger.LogWarning(exception, "Could not build the HTTP request for {RequestName}", request.Name);
				return (null, durationMilliseconds, exception.Message);
			}

			using HttpRequestMessage requestScope = attemptRequest;

			Stopwatch stopwatch = Stopwatch.StartNew();
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
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// A user cancel is not a transient failure — don't burn the remaining retries on it.
				throw;
			}
			catch (Exception exception)
			{
				stopwatch.Stop();
				durationMilliseconds = stopwatch.ElapsedMilliseconds;
				errorMessage = exception.Message;
				logger.LogWarning(exception, "Mobile request execution failed for {RequestName} on attempt {Attempt}", request.Name, attempt);

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

	private static List<ConsoleEntry> BuildMobileSafeConsoleEntries(RequestDefinition request)
	{
		bool requiresFlowScript = !string.IsNullOrWhiteSpace(request.PreRequestScript);
		bool requiresTests = !string.IsNullOrWhiteSpace(request.TestsScript);
		if (!requiresFlowScript && !requiresTests)
		{
			return [];
		}

		return
		[
			new ConsoleEntry
			{
				Level = ConsoleEntryLevel.Warning,
				Message = ScriptingWarningMessage,
			},
		];
	}
}
