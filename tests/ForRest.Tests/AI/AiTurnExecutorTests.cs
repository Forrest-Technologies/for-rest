#pragma warning disable MEAI001
#pragma warning disable OPENAI001
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ForRest.Services.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AiTurnExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_merges_incomplete_response_continuations_without_sending_a_second_prompt()
    {
        StubAgent agent = new(
            CreateResponse(
                "First part of the answer",
                ResponseStatus.Incomplete,
                ChatFinishReason.Length,
                includeContinuationToken: true),
            CreateResponse(
                " and the rest.",
                ResponseStatus.Completed,
                ChatFinishReason.Stop));
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(new StubRuntimeFactory(agent));

        AiTurnExecutionResult result = await executor.ExecuteAsync(
            new(
                ConversationId: "conversation-1",
                Objective: "Answer the prompt.",
                Prompt: "Explain the request.",
                Settings: CreateSettings()));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("First part of the answer and the rest.", result.ResponseText);
        Assert.AreEqual(2, agent.Calls.Count);
        Assert.AreEqual(1, agent.Calls[0].MessageCount);
        Assert.AreEqual(0, agent.Calls[1].MessageCount);
        Assert.IsNotNull(agent.Calls[1].ContinuationToken);
        StringAssert.Contains(result.DebugTrace, "Executor request");
        StringAssert.Contains(result.DebugTrace, "Agent prompt [initial]");
        StringAssert.Contains(result.DebugTrace, "Agent continuation [1]");
        StringAssert.Contains(result.DebugTrace, "Agent response continuation [1]");
    }

    [TestMethod]
    public async Task ExecuteAsync_reports_failure_when_response_stays_incomplete_after_automatic_continuations()
    {
        StubAgent agent = new(
            Enumerable.Range(0, 5)
                .Select(index => CreateResponse(
                    $"part-{index}",
                    ResponseStatus.Incomplete,
                    ChatFinishReason.Length,
                    includeContinuationToken: true))
                .ToArray());
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(new StubRuntimeFactory(agent));

        AiTurnExecutionResult result = await executor.ExecuteAsync(
            new(
                ConversationId: "conversation-1",
                Objective: "Answer the prompt.",
                Prompt: "Explain the request.",
                Settings: CreateSettings()));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.ResponseText, "ended before completion");
        Assert.AreEqual(5, agent.Calls.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_passes_prompt_text_to_runtime_factory_prepare()
    {
        StubAgent agent = new(CreateResponse("Done.", ResponseStatus.Completed, ChatFinishReason.Stop));
        StubRuntimeFactory runtimeFactory = new(agent);
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(runtimeFactory);

        await executor.ExecuteAsync(
            new(
                ConversationId: "conversation-2",
                Objective: "Update the active request.",
                Prompt: "Use stash rows and fully test the API surface.",
                Settings: CreateSettings()));

        Assert.AreEqual("Use stash rows and fully test the API surface.", runtimeFactory.LastPrompt);
    }

    private static AiSettings CreateSettings()
    {
        return new()
        {
            Enabled = true,
            Provider = new AiProviderSettings
            {
                ProviderKind = AiProviderKind.OpenAI,
                Transport = AiConversationTransport.ChatCompletions,
                Model = "gpt-5.1-mini",
            },
            ApiKey = new AiSecretSetting
            {
                Value = "test-key",
                IsConfigured = true,
            },
        };
    }

    private static AgentResponse CreateResponse(
        string text,
        ResponseStatus status,
        ChatFinishReason finishReason,
        bool includeContinuationToken = false)
    {
        ResponseResult rawResponse = new()
        {
            Status = status,
            Model = "gpt-5.1-mini",
        };
        rawResponse.OutputItems.Add(ResponseItem.CreateAssistantMessageItem(text));

        return new AgentResponse
        {
            FinishReason = finishReason,
            RawRepresentation = rawResponse,
            ContinuationToken = includeContinuationToken
                ? ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 })
                : null,
        };
    }

    private sealed class StubRuntimeFactory(StubAgent agent) : IAiRuntimeFactory
    {
        public string? LastPrompt { get; private set; }

        public AiPreparedRuntime Prepare(
            AiSettings settings,
            string objective,
            IAiActiveDocumentHost? activeDocumentHost = null,
            string? prompt = null)
        {
            LastPrompt = prompt;
            return new(new AiPromptManifest("test", [], []), [], agent, new AiDebugTraceBuffer());
        }
    }

    private sealed class StubAgent : AIAgent
    {
        private readonly Queue<AgentResponse> _responses;

        public StubAgent(params AgentResponse[] responses)
        {
            _responses = new Queue<AgentResponse>(responses);
        }

        public List<CallRecord> Calls { get; } = [];

        public override string? Name => "StubAgent";

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        {
            return new(new StubSession());
        }

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        {
            using JsonDocument document = JsonDocument.Parse("{}");
            return new(document.RootElement.Clone());
        }

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        {
            return new(new StubSession());
        }

        protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(messages.Count(), options?.ContinuationToken));
            return Task.FromResult(_responses.Dequeue());
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubSession : AgentSession
    {
    }

    private sealed record CallRecord(int MessageCount, ResponseContinuationToken? ContinuationToken);
}
#pragma warning restore OPENAI001
#pragma warning restore MEAI001
