#pragma warning disable MEAI001
#pragma warning disable OPENAI001
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
    bool SessionReset);

public interface IAiTurnExecutor
{
    Task<AiTurnExecutionResult> ExecuteAsync(AiTurnExecutionRequest request, CancellationToken cancellationToken = default);
}

public sealed class AgentFrameworkAiTurnExecutor : IAiTurnExecutor
{
    private const int MaxAutomaticContinuationAttempts = 4;
    private const string AutomaticContinuationPrompt = "Continue the previous answer from exactly where it stopped. Do not repeat prior text, do not add a preamble, and do not ask a follow-up question. Output only the remaining continuation.";
    private const string IncompleteResponseNote = "The AI response ended before completion after multiple automatic continuation attempts.";
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

        AiPreparedRuntime runtime = _runtimeFactory.Prepare(request.Settings, request.Objective, request.ActiveDocumentHost);
        if (runtime.Agent is null)
        {
            return new(
                Succeeded: false,
                ResponseText: BuildUnavailableMessage(runtime.Issues),
                Issues: runtime.Issues,
                SessionReset: false);
        }

        (AgentSession session, bool sessionReset) = await GetOrCreateSessionAsync(
            request.ConversationId,
            runtime.Agent,
            request.Settings,
            cancellationToken);

        try
        {
            TurnResponse turnResponse = await RunAgentWithAutomaticContinuationAsync(
                runtime.Agent,
                request.Prompt,
                session,
                cancellationToken);
            return new(
                Succeeded: turnResponse.Completed,
                ResponseText: BuildFinalResponseText(turnResponse.Text, turnResponse.Response),
                Issues: runtime.Issues,
                SessionReset: sessionReset);
        }
        catch (Exception exception)
        {
            await ResetSessionAsync(request.ConversationId);
            return new(
                Succeeded: false,
                ResponseText: $"AI request failed: {exception.Message}",
                Issues: runtime.Issues,
                SessionReset: true);
        }
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
        CancellationToken cancellationToken)
    {
        AgentResponse response = await agent.RunAsync(prompt, session, cancellationToken: cancellationToken);
        string accumulatedText = ExtractResponseText(response);

        for (int attempt = 0; attempt < MaxAutomaticContinuationAttempts && ShouldAutomaticallyContinue(response); attempt++)
        {
            response = await ContinueResponseAsync(agent, session, response, cancellationToken);
            string nextText = ExtractResponseText(response);
            accumulatedText = MergeContinuationText(accumulatedText, nextText);
        }

        return new(response, accumulatedText, Completed: !ShouldAutomaticallyContinue(response));
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
