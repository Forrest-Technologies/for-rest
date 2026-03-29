using Microsoft.Agents.AI;

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
            AgentResponse response = await runtime.Agent.RunAsync(request.Prompt, session, cancellationToken: cancellationToken);
            string responseText = ExtractResponseText(response);
            return new(
                Succeeded: true,
                ResponseText: responseText,
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

    private static string ExtractResponseText(AgentResponse response)
    {
        return string.IsNullOrWhiteSpace(response.Text)
            ? "Done."
            : response.Text.Trim();
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
}
