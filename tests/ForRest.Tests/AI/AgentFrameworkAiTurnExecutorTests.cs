#pragma warning disable MEAI001
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ForRest.Services.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AgentFrameworkAiTurnExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_continues_length_limited_response_with_an_internal_follow_up_turn()
    {
        StubAgent agent = new(
            static (_, _) => CreateResponse("First part of the answer", ChatFinishReason.Length),
            static (_, _) => CreateResponse(" and the rest.", ChatFinishReason.Stop));
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(new StubRuntimeFactory(agent));

        AiTurnExecutionResult result = await executor.ExecuteAsync(
            new(
                ConversationId: "doc-1",
                Objective: "Answer the prompt.",
                Prompt: "Summarize this request.",
                Settings: new AiSettings { Enabled = true }));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("First part of the answer and the rest.", result.ResponseText);
        Assert.AreEqual(2, agent.Calls.Count);
        Assert.AreEqual("Summarize this request.", agent.Calls[0].MessageText);
        StringAssert.Contains(agent.Calls[1].MessageText, "Continue the previous answer");
        Assert.IsFalse(agent.Calls[1].HasContinuationToken);
    }

    [TestMethod]
    public async Task ExecuteAsync_prefers_native_continuation_tokens_when_the_runtime_provides_one()
    {
        StubAgent agent = new(
            static (_, _) => CreateResponse(
                "Partial output",
                ChatFinishReason.Length,
                ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 })),
            static (_, options) =>
            {
                Assert.IsNotNull(options?.ContinuationToken);
                return CreateResponse("Partial output completed", ChatFinishReason.Stop);
            });
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(new StubRuntimeFactory(agent));

        AiTurnExecutionResult result = await executor.ExecuteAsync(
            new(
                ConversationId: "doc-2",
                Objective: "Answer the prompt.",
                Prompt: "Explain the retry flow.",
                Settings: new AiSettings { Enabled = true }));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Partial output completed", result.ResponseText);
        Assert.AreEqual(2, agent.Calls.Count);
        Assert.AreEqual("Explain the retry flow.", agent.Calls[0].MessageText);
        Assert.AreEqual(string.Empty, agent.Calls[1].MessageText);
        Assert.IsTrue(agent.Calls[1].HasContinuationToken);
    }

    private static AgentResponse CreateResponse(
        string text,
        ChatFinishReason? finishReason = null,
        ResponseContinuationToken? continuationToken = null)
    {
        return new AgentResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            FinishReason = finishReason,
            ContinuationToken = continuationToken,
        };
    }

    private sealed class StubRuntimeFactory(AIAgent agent) : IAiRuntimeFactory
    {
        public AiPreparedRuntime Prepare(AiSettings settings, string objective, IAiActiveDocumentHost? activeDocumentHost = null)
        {
            return new(new(string.Empty, [], []), [], agent);
        }
    }

    private sealed class StubAgent(params Func<IEnumerable<ChatMessage>, AgentRunOptions?, AgentResponse>[] responses) : AIAgent
    {
        private readonly Queue<Func<IEnumerable<ChatMessage>, AgentRunOptions?, AgentResponse>> _responses = new(responses);

        public List<CallInfo> Calls { get; } = [];

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<AgentSession>(new StubSession());
        }

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(JsonDocument.Parse("{}").RootElement.Clone());
        }

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<AgentSession>(new StubSession());
        }

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new(messages.Select(static message => message.Text).ToArray(), options?.ContinuationToken is not null));
            return Task.FromResult(_responses.Dequeue().Invoke(messages, options));
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubSession : AgentSession
    {
    }

    private sealed record CallInfo(IEnumerable<string> MessageTexts, bool HasContinuationToken)
    {
        public string MessageText => string.Concat(MessageTexts);
    }
}
#pragma warning restore MEAI001
