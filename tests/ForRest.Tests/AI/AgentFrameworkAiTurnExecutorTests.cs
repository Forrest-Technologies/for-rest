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

    [TestMethod]
    public async Task ExecuteAsync_retries_internally_when_an_edit_request_leaves_the_active_document_unchanged()
    {
        MutableActiveDocumentHost host = new("name \"Example\"\nmethod GET");
        StubAgent agent = new(
            (_, _) => CreateResponse("I can't safely update the script until I know the exact syntax.", ChatFinishReason.Stop),
            (_, _) =>
            {
                host.SourceText = "name \"Example\"\nmethod POST";
                return CreateResponse("Updated the request to use POST.", ChatFinishReason.Stop);
            });
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(new StubRuntimeFactory(agent));

        AiTurnExecutionResult result = await executor.ExecuteAsync(
            new(
                ConversationId: "doc-3",
                Objective: "Update the active request.",
                Prompt: "Rewrite this request to use POST.",
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Updated the request to use POST.", result.ResponseText);
        Assert.AreEqual(2, agent.Calls.Count);
        Assert.AreEqual("name \"Example\"\nmethod POST", host.SourceText);
        StringAssert.Contains(agent.Calls[1].MessageText, "The previous turn did not modify the active document");
    }

    [TestMethod]
    public async Task ExecuteAsync_treats_enumerate_and_stash_prompt_as_an_in_place_edit_request()
    {
        MutableActiveDocumentHost host = new("name \"Get Todo\"\nmethod GET\nurl \"https://jsonplaceholder.typicode.com/todos/1\"");
        StubAgent agent = new(
            (_, _) => CreateResponse("I need to know whether to replace this request or create a second one.", ChatFinishReason.Stop),
            (_, _) =>
            {
                host.SourceText =
                    "name \"Get Todo\"\nmethod GET\nurl \"https://jsonplaceholder.typicode.com/todos/1\"\nmax_send_iterations 20\n\nforeach todoId in [1..20] {\n  request.url = $\"https://jsonplaceholder.typicode.com/todos/{todoId}\"\n  let sent = request.send()\n  if sent.completed {\n    stash.UserId = sent.userId\n    stash.TodoId = sent.id\n    stash.Title = sent.title\n    stash.Commit()\n  }\n}\n\nexpect status == 200 \"returns 200\"";
                return CreateResponse("Updated the active request to iterate todos and stash completed rows.", ChatFinishReason.Stop);
            });
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(new StubRuntimeFactory(agent));

        AiTurnExecutionResult result = await executor.ExecuteAsync(
            new(
                ConversationId: "doc-3b",
                Objective: "Update the active request.",
                Prompt: "Enumerate over 1 through 20, for the completed ones, stash the user ID and the Id and the Message.",
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(2, agent.Calls.Count);
        StringAssert.Contains(agent.Calls[1].MessageText, "Modify the current active request in place.");
        StringAssert.Contains(agent.Calls[1].MessageText, "foreach/request.send/request.url/max_send_iterations pattern");
        StringAssert.Contains(host.SourceText, "max_send_iterations 20");
        StringAssert.Contains(host.SourceText, "foreach todoId in [1..20]");
        StringAssert.Contains(host.SourceText, "stash.Title = sent.title");
    }

    [TestMethod]
    public async Task ExecuteAsync_recovery_prompt_prefers_loop_flow_for_repeated_patch_requests()
    {
        MutableActiveDocumentHost host = new("name \"Example\"\nmethod PATCH\nurl \"https://api.restful-api.dev/objects/1\"");
        StubAgent agent = new(
            (_, _) => CreateResponse("The editor rejected the change before the document was updated.", ChatFinishReason.Stop),
            (_, _) =>
            {
                host.SourceText =
                    "name \"Example\"\nmethod PATCH\nurl \"https://api.restful-api.dev/objects/1\"\nmax_send_iterations 3\n\nruntime trace_id = guid()\n\nforeach attempt in [1..3] {\n  request.method = \"PATCH\"\n  request.url = \"https://api.restful-api.dev/objects/1\"\n  request.content_type = \"application/json\"\n  request.body = $\"{{\\\"name\\\":\\\"Patch {attempt} {trace_id}\\\"}}\"\n  let sent = request.send()\n}";
                return CreateResponse("Updated the request to loop over three patch attempts.", ChatFinishReason.Stop);
            });
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(new StubRuntimeFactory(agent));

        AiTurnExecutionResult result = await executor.ExecuteAsync(
            new(
                ConversationId: "doc-3b-loop",
                Objective: "Update the active request.",
                Prompt: "Perform the patch at least 3 times with at least 1 randomized value.",
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(2, agent.Calls.Count);
        Assert.AreEqual(1, result.AutonomousEditRecoveryAttempts);
        StringAssert.Contains(agent.Calls[1].MessageText, "repeat N times");
        StringAssert.Contains(agent.Calls[1].MessageText, "foreach/range/max_send_iterations");
        StringAssert.Contains(agent.Calls[1].MessageText, "Do not invent Math.*");
        StringAssert.Contains(agent.Calls[1].MessageText, ".Substring(...)");
        StringAssert.Contains(host.SourceText, "foreach attempt in [1..3]");
    }

    [TestMethod]
    public async Task ExecuteAsync_retries_internal_repair_for_edit_prompts_that_start_with_what_i_want_you_to_do()
    {
        MutableActiveDocumentHost host = new("name \"Example\"\nmethod GET");
        StubAgent agent = new(
            (_, _) => CreateResponse(
                "I updated the active request to hit `https://api.restful-api.dev/objects`, but the script currently does not run in the editor because the runtime error indicates the parsed JSON object type does not have a `data` field in this environment. If you want, I can fix it next by reading the response JSON structure and adjusting the path.",
                ChatFinishReason.Stop),
            (_, _) =>
            {
                host.SourceText =
                    "name \"Example\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"\nmax_send_iterations 50";
                return CreateResponse("Updated the request to use the objects endpoint.", ChatFinishReason.Stop);
            });
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(new StubRuntimeFactory(agent));

        AiTurnExecutionResult result = await executor.ExecuteAsync(
            new(
                ConversationId: "doc-3c",
                Objective: "Update the active request.",
                Prompt:
                    "What I want you to do is make a script that hits https://api.restful-api.dev/objects. Stash the ID, name up to 10 characters, and price for matching objects.",
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(2, agent.Calls.Count);
        Assert.AreEqual("name \"Example\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"\nmax_send_iterations 50", host.SourceText);
        StringAssert.Contains(agent.Calls[1].MessageText, "The previous turn did not modify the active document");
    }

    [TestMethod]
    public async Task ExecuteAsync_forces_full_replacement_for_exact_canvas_rejection_reply_from_user_repro()
    {
        MutableActiveDocumentHost host = new("name \"Example\"\nmethod GET\nurl \"https://jsonplaceholder.typicode.com/posts/1\"");
        StubAgent agent = new(
            (_, _) => CreateResponse(
                "I can\u2019t apply the requested change in this canvas: the active ForRest script remains unchanged (still `url \"https://jsonplaceholder.typicode.com/posts/1\"`), and any attempted full rewrite to `https://api.restful-api.dev/objects` with the A-C + `price > 300` filter and \u226450-result stash is rejected by the editor as \"not runnable\" due to parse errors (including `; expected` on rewritten `header ...` lines / `expect` placement constraints).",
                ChatFinishReason.Stop),
            (_, _) =>
            {
                host.SourceText =
                    "name \"Example\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"\nmax_send_iterations 50";
                return CreateResponse("Replaced the active request with the working objects script.", ChatFinishReason.Stop);
            });
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(new StubRuntimeFactory(agent));

        AiTurnExecutionResult result = await executor.ExecuteAsync(
            new(
                ConversationId: "doc-3d",
                Objective: "Update the active request.",
                Prompt:
                    "Okay, I want you to make a script that hits https://api.restful-api.dev/objects. Find objects that start with A-C and cost more than 300, then stash up to 50 rows.",
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(2, agent.Calls.Count);
        Assert.AreEqual("name \"Example\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"\nmax_send_iterations 50", host.SourceText);
        StringAssert.Contains(agent.Calls[1].MessageText, "full replace_active_document call");
        StringAssert.Contains(agent.Calls[1].MessageText, "The previous turn did not modify the active document");
        StringAssert.Contains(agent.Calls[1].MessageText, "request-send/request.method/request.url/request.headers/request.body/request.content_type/api-surface-crud/stash/top-level expect patterns");
        StringAssert.Contains(agent.Calls[1].MessageText, "leave only runnable ForRest source");
        StringAssert.Contains(agent.Calls[1].MessageText, "replace that target instead of leaving the previous URL or method in place");
    }

    [TestMethod]
    public async Task ExecuteAsync_does_not_force_internal_repair_for_non_edit_prompts()
    {
        MutableActiveDocumentHost host = new("name \"Example\"\nmethod GET");
        StubAgent agent = new(
            (_, _) => CreateResponse("This request sends a GET request.", ChatFinishReason.Stop));
        IAiTurnExecutor executor = new AgentFrameworkAiTurnExecutor(new StubRuntimeFactory(agent));

        AiTurnExecutionResult result = await executor.ExecuteAsync(
            new(
                ConversationId: "doc-4",
                Objective: "Explain the active request.",
                Prompt: "Explain what this request does.",
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("This request sends a GET request.", result.ResponseText);
        Assert.AreEqual(1, agent.Calls.Count);
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
        public AiPreparedRuntime Prepare(
            AiSettings settings,
            string objective,
            IAiActiveDocumentHost? activeDocumentHost = null,
            string? prompt = null)
        {
            return new(new(string.Empty, [], []), [], agent, new AiDebugTraceBuffer());
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

    private sealed class MutableActiveDocumentHost(string sourceText) : IAiActiveDocumentHost
    {
        public string SourceText { get; set; } = sourceText;

        public AiActiveDocumentSnapshot? GetActiveDocument()
        {
            return new(
                "request-1",
                "Example Request",
                "forrest",
                SourceText,
                []);
        }

        public AiActiveDocumentUpdateResult UpdateActiveDocument(AiActiveDocumentSnapshot document, string updatedText)
        {
            SourceText = updatedText;
            return AiActiveDocumentUpdateResult.Success(updatedText);
        }
    }

    private sealed record CallInfo(IEnumerable<string> MessageTexts, bool HasContinuationToken)
    {
        public string MessageText => string.Concat(MessageTexts);
    }
}
#pragma warning restore MEAI001
