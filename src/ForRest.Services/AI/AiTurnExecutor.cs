#pragma warning disable MEAI001
#pragma warning disable OPENAI001
using System.ClientModel;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace ForRest.Services.AI;

public sealed record AiTurnExecutionRequest(
    string ConversationId,
    string Objective,
    string Prompt,
    AiSettings Settings,
    IAiActiveDocumentHost? ActiveDocumentHost = null);

public sealed record AiTurnExecutionResult(
    bool Succeeded,
    string ResponseText,
    IReadOnlyList<AiSettingsIssue> Issues,
    bool SessionReset,
    int AutonomousEditRecoveryAttempts = 0,
    bool DeterministicFallbackApplied = false,
    string DebugTrace = "");

public interface IAiTurnExecutor
{
    Task<AiTurnExecutionResult> ExecuteAsync(AiTurnExecutionRequest request, CancellationToken cancellationToken = default);
}

public sealed class AgentFrameworkAiTurnExecutor : IAiTurnExecutor
{
    private const int MaxAutomaticContinuationAttempts = 4;
    private const int MaxAutonomousEditRecoveryAttempts = 1;
    private const int MaxTransientNetworkRetryAttempts = 3;
    private static readonly TimeSpan InitialTransientNetworkRetryDelay = TimeSpan.FromSeconds(1);
    private const string AutomaticContinuationPrompt = "Continue the previous answer from exactly where it stopped. Do not repeat prior text, do not add a preamble, and do not ask a follow-up question. Output only the remaining continuation.";
    private const string IncompleteResponseNote = "The AI response ended before completion after multiple automatic continuation attempts.";
    private const string TimedOutResponseNote = "AI request timed out before completion.";
    private readonly IAiRuntimeFactory _runtimeFactory;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly Dictionary<string, ConversationSessionEntry> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public AgentFrameworkAiTurnExecutor(IAiRuntimeFactory runtimeFactory)
    {
        _runtimeFactory = runtimeFactory;
    }

    public async Task<AiTurnExecutionResult> ExecuteAsync(AiTurnExecutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AiPreparedRuntime runtime = _runtimeFactory.Prepare(
            request.Settings,
            request.Objective,
            request.ActiveDocumentHost,
            request.Prompt);
        runtime.DebugTrace.AddSection(
            "Executor request",
            string.Join(
                Environment.NewLine,
                [
                    $"ConversationId: {request.ConversationId}",
                    $"Objective: {request.Objective}",
                    "Prompt:",
                    request.Prompt,
                ]));
        if (runtime.Agent is null)
        {
            return new(
                Succeeded: false,
                ResponseText: BuildUnavailableMessage(runtime.Issues),
                Issues: runtime.Issues,
                SessionReset: false,
                AutonomousEditRecoveryAttempts: 0,
                DebugTrace: runtime.DebugTrace.Snapshot());
        }

        // When targeting Grok, install an AsyncLocal capture buffer so the
        // compatibility policy can record the exact (sanitized) request
        // body the SDK sent to xAI. We flush that buffer into the debug
        // trace after the call — regardless of success / failure — so
        // the outbound payload is always auditable next to whatever
        // response (or 400 body) came back.
        bool captureGrokBody = request.Settings.Provider.ProviderKind == AiProviderKind.Grok;
        if (captureGrokBody)
        {
            AgentFrameworkAiRuntimeFactory.GrokCompatibilityPolicy.LastOutboundBody.Value = new StringBuilder();
        }

        try
        {
            (AgentSession session, bool sessionReset) = await GetOrCreateSessionAsync(
                request.ConversationId,
                runtime.Agent,
                request.Settings,
                cancellationToken);
            runtime.DebugTrace.AddLine($"Session reset before turn: {sessionReset}");
            AiActiveDocumentSnapshot? initialDocument = request.ActiveDocumentHost?.GetActiveDocument();
            TurnExecutionOutcome turnOutcome = await RunAgentWithAutonomousEditRecoveryAsync(
                runtime.Agent,
                request,
                session,
                runtime.DebugTrace,
                cancellationToken);
            AiActiveDocumentSnapshot? latestDocument = request.ActiveDocumentHost?.GetActiveDocument();
            bool documentChangedDuringTurn =
                initialDocument is not null &&
                latestDocument is not null &&
                HasActiveDocumentChanged(initialDocument.SourceText, latestDocument.SourceText);
            bool editCompleted = !ShouldAttemptAutonomousEditRecovery(
                request,
                initialDocument,
                latestDocument);
            // A length-truncated response after the document was already updated
            // still represents a successful edit from the user's point of view.
            bool succeeded = (turnOutcome.Response.Completed && editCompleted) ||
                             (editCompleted && documentChangedDuringTurn);
            runtime.DebugTrace.AddLine(
                $"Turn success evaluation: responseCompleted={turnOutcome.Response.Completed} editCompleted={editCompleted} documentChangedDuringTurn={documentChangedDuringTurn} succeeded={succeeded}");
            FlushGrokOutboundBody(runtime, captureGrokBody);
            return new(
                Succeeded: succeeded,
                ResponseText: BuildFinalResponseText(turnOutcome.Response.Text, turnOutcome.Response.Response),
                Issues: runtime.Issues,
                SessionReset: sessionReset,
                AutonomousEditRecoveryAttempts: turnOutcome.AutonomousEditRecoveryAttempts,
                DebugTrace: runtime.DebugTrace.Snapshot());
        }
        catch (TaskCanceledException)
        {
            await ResetSessionAsync(request.ConversationId);
            runtime.DebugTrace.AddLine("Executor result: timed out and reset the session.");
            FlushGrokOutboundBody(runtime, captureGrokBody);
            return new(
                Succeeded: false,
                ResponseText: TimedOutResponseNote,
                Issues: runtime.Issues,
                SessionReset: true,
                AutonomousEditRecoveryAttempts: 0,
                DebugTrace: runtime.DebugTrace.Snapshot());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ResetSessionAsync(request.ConversationId);
            runtime.DebugTrace.AddLine("Executor result: canceled by caller and reset the session.");
            FlushGrokOutboundBody(runtime, captureGrokBody);
            return new(
                Succeeded: false,
                ResponseText: TimedOutResponseNote,
                Issues: runtime.Issues,
                SessionReset: true,
                AutonomousEditRecoveryAttempts: 0,
                DebugTrace: runtime.DebugTrace.Snapshot());
        }
        catch (Exception exception)
        {
            await ResetSessionAsync(request.ConversationId);
            string providerDetail = TryExtractProviderErrorDetail(exception);

            // Surface the 400 body FIRST in the debug trace so it is the
            // first thing a human scrolling the Inspect panel sees after
            // a failure, then follow it with the full exception.
            if (!string.IsNullOrWhiteSpace(providerDetail))
            {
                runtime.DebugTrace.AddSection("Provider error body", providerDetail);
            }

            runtime.DebugTrace.AddSection("Executor exception", exception.ToString());
            FlushGrokOutboundBody(runtime, captureGrokBody);

            string userMessage = string.IsNullOrWhiteSpace(providerDetail)
                ? $"AI request failed: {exception.Message}"
                : $"AI request failed: {exception.Message}{Environment.NewLine}Provider response body:{Environment.NewLine}{providerDetail}";

            return new(
                Succeeded: false,
                ResponseText: userMessage,
                Issues: runtime.Issues,
                SessionReset: true,
                AutonomousEditRecoveryAttempts: 0,
                DebugTrace: runtime.DebugTrace.Snapshot());
        }
    }

    private static void FlushGrokOutboundBody(AiPreparedRuntime runtime, bool captureGrokBody)
    {
        if (!captureGrokBody)
        {
            return;
        }

        StringBuilder? buffer = AgentFrameworkAiRuntimeFactory.GrokCompatibilityPolicy.LastOutboundBody.Value;
        AgentFrameworkAiRuntimeFactory.GrokCompatibilityPolicy.LastOutboundBody.Value = null;
        if (buffer is null || buffer.Length == 0)
        {
            return;
        }

        runtime.DebugTrace.AddSection(
            "Outbound Grok request body (post-sanitize)",
            buffer.ToString());
    }

    /// <summary>
    /// When the underlying OpenAI-compatible transport throws, the real
    /// failure detail (e.g. xAI / Groq / Anthropic's 400-body explaining
    /// exactly which field was rejected) lives on
    /// <see cref="ClientResultException.GetRawResponse"/> — not
    /// <see cref="Exception.Message"/>, which is always the terse
    /// "Service request failed. Status: ...". Walk the full inner-exception
    /// chain because <c>FunctionInvokingChatClient</c> and
    /// <c>ChatClientAgent</c> wrap the original SDK exception a few layers
    /// deep before it reaches us.
    /// </summary>
    private static string TryExtractProviderErrorDetail(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ClientResultException clientResultException)
            {
                try
                {
                    var raw = clientResultException.GetRawResponse();
                    if (raw is null)
                    {
                        continue;
                    }

                    string? body = raw.Content?.ToString();
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        continue;
                    }

                    // Keep the surfaced detail bounded — some providers
                    // echo request payloads back in their error responses.
                    return body.Length > 4_096 ? body[..4_096] + "…" : body;
                }
                catch
                {
                    // If we can't read the body for any reason (stream
                    // already consumed, buffer disposed, etc.) fall back
                    // to the next layer.
                }
            }
        }

        return string.Empty;
    }

    private async Task<(AgentSession Session, bool SessionReset)> GetOrCreateSessionAsync(
        string conversationId,
        AIAgent agent,
        AiSettings settings,
        CancellationToken cancellationToken)
    {
        string key = string.IsNullOrWhiteSpace(conversationId) ? "default" : conversationId.Trim();
        string sessionKey = BuildSessionKey(settings);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan idleTimeout = TimeSpan.FromMinutes(Math.Max(1, settings.Conversation.IdleTimeoutMinutes));

        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryGetValue(key, out ConversationSessionEntry? existing))
            {
                bool expired = now - existing.LastUsedUtc > idleTimeout;
                bool configurationChanged = !string.Equals(existing.SessionKey, sessionKey, StringComparison.Ordinal);
                if (!expired && !configurationChanged)
                {
                    existing.LastUsedUtc = now;
                    return (existing.Session, SessionReset: false);
                }

                _sessions.Remove(key);
            }

            AgentSession session = await agent.CreateSessionAsync(cancellationToken);
            _sessions[key] = new(session, now, sessionKey);
            return (session, SessionReset: true);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task ResetSessionAsync(string conversationId)
    {
        string key = string.IsNullOrWhiteSpace(conversationId) ? "default" : conversationId.Trim();
        await _sessionGate.WaitAsync();
        try
        {
            _sessions.Remove(key);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private static string BuildSessionKey(AiSettings settings)
    {
        return string.Join(
            "|",
            settings.Enabled,
            settings.Provider.ProviderKind,
            settings.Provider.Transport,
            settings.Provider.Endpoint,
            settings.Provider.Model,
            settings.Provider.DeploymentName);
    }

    private static string BuildUnavailableMessage(IReadOnlyList<AiSettingsIssue> issues)
    {
        if (issues.Count == 0)
        {
            return "AI is not available for this request.";
        }

        return string.Join(
            Environment.NewLine,
            issues.Select(issue => issue.Message));
    }

    private static async Task<TurnResponse> RunAgentWithAutomaticContinuationAsync(
        AIAgent agent,
        string prompt,
        AgentSession session,
        AiDebugTraceBuffer debugTrace,
        CancellationToken cancellationToken)
    {
        debugTrace.AddSection("Agent prompt [initial]", prompt);
        AgentResponse response = await RunWithTransientNetworkRetryAsync(
            () => agent.RunAsync(prompt, session, cancellationToken: cancellationToken),
            "Agent prompt [initial]",
            debugTrace,
            cancellationToken);
        string accumulatedText = ExtractResponseText(response);
        debugTrace.AddSection("Agent response [initial]", DescribeAgentResponse(response, accumulatedText));

        for (int attempt = 0; attempt < MaxAutomaticContinuationAttempts && ShouldAutomaticallyContinue(response); attempt++)
        {
            debugTrace.AddSection(
                $"Agent continuation [{attempt + 1}]",
                response.ContinuationToken is not null
                    ? "Used provider continuation token."
                    : AutomaticContinuationPrompt);
            AgentResponse priorResponse = response;
            response = await RunWithTransientNetworkRetryAsync(
                () => ContinueResponseAsync(agent, session, priorResponse, cancellationToken),
                $"Agent continuation [{attempt + 1}]",
                debugTrace,
                cancellationToken);
            string nextText = ExtractResponseText(response);
            accumulatedText = MergeContinuationText(accumulatedText, nextText);
            debugTrace.AddSection($"Agent response continuation [{attempt + 1}]", DescribeAgentResponse(response, nextText));
        }

        return new(response, accumulatedText, Completed: !ShouldAutomaticallyContinue(response));
    }

    private static async Task<T> RunWithTransientNetworkRetryAsync<T>(
        Func<Task<T>> operation,
        string operationLabel,
        AiDebugTraceBuffer debugTrace,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = InitialTransientNetworkRetryDelay;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception exception) when (attempt < MaxTransientNetworkRetryAttempts && IsTransientNetworkFailure(exception))
            {
                debugTrace.AddLine(
                    $"Transient network failure on {operationLabel} attempt {attempt}: {exception.GetType().Name} - {exception.Message}. Retrying in {delay.TotalSeconds:0.#}s.");
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    throw;
                }

                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 8000));
            }
        }
    }

    private static bool IsTransientNetworkFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case HttpRequestException httpRequestException
                    when httpRequestException.StatusCode is null ||
                         httpRequestException.StatusCode == HttpStatusCode.RequestTimeout ||
                         httpRequestException.StatusCode == HttpStatusCode.TooManyRequests ||
                         (int)httpRequestException.StatusCode.Value >= 500:
                    return true;
                case SocketException:
                    return true;
                case IOException ioException when ioException is not FileNotFoundException and not DirectoryNotFoundException:
                    return true;
                case TimeoutException:
                    return true;
            }
        }

        return false;
    }

    private static async Task<TurnExecutionOutcome> RunAgentWithAutonomousEditRecoveryAsync(
        AIAgent agent,
        AiTurnExecutionRequest request,
        AgentSession session,
        AiDebugTraceBuffer debugTrace,
        CancellationToken cancellationToken)
    {
        AiActiveDocumentSnapshot? initialDocument = request.ActiveDocumentHost?.GetActiveDocument();
        TurnResponse response = await RunAgentWithAutomaticContinuationAsync(
            agent,
            request.Prompt,
            session,
            debugTrace,
            cancellationToken);
        int recoveryAttempts = 0;

        if (!ShouldAttemptAutonomousEditRecovery(
                request,
                initialDocument,
                request.ActiveDocumentHost?.GetActiveDocument()))
        {
            debugTrace.AddLine("Autonomous edit recovery: not needed after the initial turn.");
            return new(response, recoveryAttempts);
        }

        for (int attempt = 0; attempt < MaxAutonomousEditRecoveryAttempts; attempt++)
        {
            recoveryAttempts++;
            string repairPrompt = BuildAutonomousEditRecoveryPrompt(request.Prompt, response.Text, attempt + 1);
            debugTrace.AddSection($"Autonomous edit recovery prompt [{attempt + 1}]", repairPrompt);
            response = await RunAgentWithAutomaticContinuationAsync(
                agent,
                repairPrompt,
                session,
                debugTrace,
                cancellationToken);
            if (!ShouldAttemptAutonomousEditRecovery(
                    request,
                    initialDocument,
                    request.ActiveDocumentHost?.GetActiveDocument()))
            {
                debugTrace.AddLine($"Autonomous edit recovery: attempt {attempt + 1} updated the document.");
                break;
            }
        }

        if (recoveryAttempts > 0 &&
            ShouldAttemptAutonomousEditRecovery(
                request,
                initialDocument,
                request.ActiveDocumentHost?.GetActiveDocument()))
        {
            debugTrace.AddLine("Autonomous edit recovery: exhausted retry budget with the document still unchanged.");
        }

        return new(response, recoveryAttempts);
    }

    private static Task<AgentResponse> ContinueResponseAsync(
        AIAgent agent,
        AgentSession session,
        AgentResponse response,
        CancellationToken cancellationToken)
    {
        if (response.ContinuationToken is not null)
        {
            return agent.RunAsync(
                session,
                new AgentRunOptions
                {
                    ContinuationToken = response.ContinuationToken,
                },
                cancellationToken);
        }

        return agent.RunAsync(AutomaticContinuationPrompt, session, cancellationToken: cancellationToken);
    }

    private static bool ShouldAutomaticallyContinue(AgentResponse response)
    {
        if (response.FinishReason == ChatFinishReason.Length)
        {
            return true;
        }

        return response.RawRepresentation is ResponseResult rawResponse &&
               rawResponse.Status == ResponseStatus.Incomplete &&
               rawResponse.IncompleteStatusDetails?.Reason == ResponseIncompleteStatusReason.MaxOutputTokens;
    }

    private static string ExtractResponseText(AgentResponse response)
    {
        if (!string.IsNullOrEmpty(response.Text))
        {
            return response.Text;
        }

        if (response.RawRepresentation is ResponseResult rawResponse)
        {
            return rawResponse.GetOutputText() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string MergeContinuationText(string currentText, string continuationText)
    {
        if (string.IsNullOrEmpty(currentText))
        {
            return continuationText ?? string.Empty;
        }

        if (string.IsNullOrEmpty(continuationText))
        {
            return currentText;
        }

        if (continuationText.StartsWith(currentText, StringComparison.Ordinal))
        {
            return continuationText;
        }

        if (currentText.StartsWith(continuationText, StringComparison.Ordinal))
        {
            return currentText;
        }

        int overlapLength = FindOverlapLength(currentText, continuationText);
        return overlapLength <= 0
            ? currentText + continuationText
            : currentText + continuationText[overlapLength..];
    }

    private static bool ShouldAttemptAutonomousEditRecovery(
        AiTurnExecutionRequest request,
        AiActiveDocumentSnapshot? initialDocument,
        AiActiveDocumentSnapshot? latestDocument)
    {
        return request.ActiveDocumentHost is not null &&
               initialDocument is not null &&
               latestDocument is not null &&
               IsLikelyEditPrompt(request.Prompt) &&
               !HasActiveDocumentChanged(initialDocument.SourceText, latestDocument.SourceText);
    }

    private static bool IsLikelyEditPrompt(string prompt)
    {
        return AiPromptIntentClassifier.IsLikelyEditPrompt(prompt);
    }

    private static bool HasActiveDocumentChanged(string? initialSource, string? latestSource)
    {
        return !string.Equals(
            NormalizeComparisonText(initialSource),
            NormalizeComparisonText(latestSource),
            StringComparison.Ordinal);
    }

    private static string NormalizeComparisonText(string? sourceText)
    {
        return (sourceText ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private static string NormalizeSearchText(string? value)
    {
        return NormalizeComparisonText(value)
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u2264', '<')
            .Replace('\u2265', '>')
            .Replace("\u00E2\u20AC\u2122", "'", StringComparison.Ordinal)
            .Replace("\u00E2\u20AC\u02DC", "'", StringComparison.Ordinal)
            .Replace("\u00E2\u20AC\u0153", "\"", StringComparison.Ordinal)
            .Replace("\u00E2\u20AC\u009D", "\"", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string BuildAutonomousEditRecoveryPrompt(string originalPrompt, string latestResponseText, int attemptNumber)
    {
        string normalizedLatestResponse = NormalizeComparisonText(latestResponseText);
        string searchableLatestResponse = NormalizeSearchText(latestResponseText);
        bool forceFullReplace =
            attemptNumber > 1 ||
            searchableLatestResponse.Contains("still not updated", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("too brittle", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("replace the entire active document", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("replace the whole script", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("replace the whole document", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("if you allow one action", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("safe version", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("in-place patch", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("requested change in this canvas", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("rejected by the editor", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("remains unchanged", StringComparison.Ordinal) ||
            searchableLatestResponse.Contains("not runnable", StringComparison.Ordinal);
        string replacementDirective = forceFullReplace
            ? "Your next document mutation must be a full replace_active_document call with the final working request." + Environment.NewLine +
              "Do not propose a partial logging probe, a 'safe' intermediate version, or an extra permission step." + Environment.NewLine +
              "Do not tell the user the document is too brittle to patch; replace it now with the corrected request." + Environment.NewLine
            : string.Empty;
        string latestFailureSection = string.IsNullOrWhiteSpace(normalizedLatestResponse)
            ? string.Empty
            : "Latest failed reply (do not repeat it back to the user):" + Environment.NewLine +
              normalizedLatestResponse + Environment.NewLine + Environment.NewLine;

        return
            "The previous turn did not modify the active document, but the user asked for an edit." + Environment.NewLine +
            Environment.NewLine +
            "Do not explain the failed intermediate attempt." + Environment.NewLine +
            "Do not ask the user a clarification question unless a real product decision is impossible to infer." + Environment.NewLine +
            "Read the active document again, inspect any returned diagnostics or tool errors, consult local docs if needed, and apply a working change now." + Environment.NewLine +
            "Modify the current active request in place. Do not ask whether to create a second request unless the user explicitly asked for an additional request." + Environment.NewLine +
            "If the user asked to iterate, enumerate, batch, or stash values, use the documented foreach/request.send/request.url/max_send_iterations pattern instead of asking how to structure it." + Environment.NewLine +
            "Phrases like '3 times', 'repeat N times', or 'at least N times' are loop requests. Prefer documented foreach/range/max_send_iterations flow over manually duplicating similar request blocks." + Environment.NewLine +
            "If the user asked to test an API surface or multiple methods, use the documented request-send/request.method/request.url/request.headers/request.body/request.content_type/api-surface-crud/stash/top-level expect patterns." + Environment.NewLine +
            "If the user pasted API docs or prose into chat, treat that text as requirements only and leave only runnable ForRest source in the final document." + Environment.NewLine +
            "If the current request still points at the old endpoint, replace that target instead of leaving the previous URL or method in place." + Environment.NewLine +
            "If a requested field name looks misspelled but the nearest valid field is obvious, choose the closest valid field and mention that assumption only after the edit succeeds." + Environment.NewLine +
            "If the user needs randomized or unique values, use only documented ForRest helpers from local docs such as guid() runtime values plus documented strings/convert/time helpers. Do not invent Math.*, instance methods like .Substring(...), or arbitrary C# APIs." + Environment.NewLine +
            "Prefer response.someField or response[\"Some Field\"] for JSON object members." + Environment.NewLine +
            "When the response body root is an array, iterate response directly or use response[index]." + Environment.NewLine +
            "response.json() returns a raw JsonNode; use it only with explicit indexers or AsArray(), not dot-member access." + Environment.NewLine +
            "For top-level smoke-test assertions, use exact forms like `expect status == 200 \"returns 200\"` and `expect header \"Content-Type\" contains \"json\" \"json response\"`; do not emit `expect sent.status == 200`." + Environment.NewLine +
            replacementDirective +
            "If patch_active_document fails or the structure is brittle, use replace_active_document with the full corrected request." + Environment.NewLine +
            "If replace_active_document is rejected, repair the full source and try replace_active_document again." + Environment.NewLine +
            "Finish with a brief statement of what you changed only after the document has actually been updated." + Environment.NewLine +
            Environment.NewLine +
            latestFailureSection +
            "Original user request:" + Environment.NewLine +
            originalPrompt.Trim();
    }

    private static int FindOverlapLength(string existingText, string continuationText)
    {
        int maxOverlap = Math.Min(Math.Min(existingText.Length, continuationText.Length), 240);
        for (int overlapLength = maxOverlap; overlapLength > 0; overlapLength--)
        {
            if (existingText.AsSpan(existingText.Length - overlapLength).SequenceEqual(continuationText.AsSpan(0, overlapLength)))
            {
                return overlapLength;
            }
        }

        return 0;
    }

    private static string BuildFinalResponseText(string responseText, AgentResponse response)
    {
        string normalizedText = string.IsNullOrWhiteSpace(responseText)
            ? string.Empty
            : responseText.Trim();
        if (ShouldAutomaticallyContinue(response))
        {
            normalizedText = string.IsNullOrWhiteSpace(normalizedText)
                ? IncompleteResponseNote
                : normalizedText + Environment.NewLine + Environment.NewLine + $"[{IncompleteResponseNote}]";
        }

        return string.IsNullOrWhiteSpace(normalizedText)
            ? "Done."
            : normalizedText;
    }

    private static string DescribeAgentResponse(AgentResponse response, string responseText)
    {
        List<string> lines =
        [
            $"Finish reason: {response.FinishReason?.ToString() ?? "(none)"}",
            $"Continuation token: {(response.ContinuationToken is null ? "no" : "yes")}",
        ];

        if (response.RawRepresentation is ResponseResult rawResponse)
        {
            lines.Add($"Raw status: {rawResponse.Status}");
            if (rawResponse.IncompleteStatusDetails is not null)
            {
                lines.Add($"Incomplete reason: {rawResponse.IncompleteStatusDetails.Reason}");
            }
        }

        lines.Add("Response text:");
        lines.Add(AiDebugTraceBuffer.Truncate(responseText, 8000));
        return string.Join(Environment.NewLine, lines);
    }

    private sealed record TurnExecutionOutcome(TurnResponse Response, int AutonomousEditRecoveryAttempts);

    private sealed class ConversationSessionEntry
    {
        public ConversationSessionEntry(AgentSession session, DateTimeOffset lastUsedUtc, string sessionKey)
        {
            Session = session;
            LastUsedUtc = lastUsedUtc;
            SessionKey = sessionKey;
        }

        public AgentSession Session { get; }

        public DateTimeOffset LastUsedUtc { get; set; }

        public string SessionKey { get; }
    }

    private sealed record TurnResponse(AgentResponse Response, string Text, bool Completed);
}
#pragma warning restore MEAI001
#pragma warning restore OPENAI001
