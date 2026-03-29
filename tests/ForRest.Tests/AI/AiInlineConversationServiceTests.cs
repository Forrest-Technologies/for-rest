using System.Collections.Generic;
using ForRest.Services.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AiInlineConversationServiceTests
{
    [TestMethod]
    public async Task TryHandleAsync_returns_not_handled_when_cursor_is_not_on_or_after_ai_prompt()
    {
        IAiInlineConversationService service = new AiInlineConversationService(new StubTurnExecutor());
        StubActiveDocumentHost host = new("name \"demo\"\nmethod GET");

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsFalse(result.Handled);
    }

    [TestMethod]
    public async Task TryHandleAsync_keeps_only_current_conversation_and_opens_a_fresh_prompt()
    {
        IAiInlineConversationService service = new AiInlineConversationService(new StubTurnExecutor("Applied the fix."));
        string source = string.Join(
            "\n",
            [
                "## first task",
                "#> first answer",
                "method GET",
                "## second task",
            ]);
        StubActiveDocumentHost host = new(source);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 4,
                Settings: new AiSettings
                {
                    Enabled = true,
                    Conversation = new AiConversationSettings
                    {
                        MaxHistoryTurns = 1,
                    },
                },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.UpdatedText.Contains("## first task", StringComparison.Ordinal));
        StringAssert.Contains(result.UpdatedText, "#> Applied the fix.");
        StringAssert.Contains(result.UpdatedText, "\n## ");
    }

    [TestMethod]
    public async Task TryHandleAsync_handles_trailing_prompt_after_user_pressed_enter()
    {
        IAiInlineConversationService service = new AiInlineConversationService(new StubTurnExecutor("Yes."));
        string source = "name \"demo\"\n## Do you work agent?\n";
        StubActiveDocumentHost host = new(source);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 3,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.UpdatedText, "## Do you work agent?");
        StringAssert.Contains(result.UpdatedText, "#> Yes.");
        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## Do you work agent?",
                "#> Yes.",
                string.Empty,
                "## "
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(5, result.SuggestedCursorLineNumber);
        Assert.AreEqual(4, result.SuggestedCursorColumn);
    }

    [TestMethod]
    public async Task TryHandleAsync_returns_not_handled_for_blank_trailing_prompt()
    {
        IAiInlineConversationService service = new AiInlineConversationService(new StubTurnExecutor("Should not run."));
        string source = "name \"demo\"\nmethod GET\n\n## ";
        StubActiveDocumentHost host = new(source);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 4,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsFalse(result.Handled);
        Assert.AreEqual(source, result.UpdatedText);
    }

    [TestMethod]
    public async Task TryHandleAsync_handles_reset_command_without_invoking_ai_turn_executor()
    {
        StubTurnExecutor executor = new("Should not run.");
        IAiInlineConversationService service = new AiInlineConversationService(executor);
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## first task",
                "#> first answer",
                "method GET",
                "## reset",
            ]);
        StubActiveDocumentHost host = new(source);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 5,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, executor.CallCount);
        Assert.AreEqual("AI history reset.", result.StatusText);
        StringAssert.Contains(result.DebugText, "Command: reset");
        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "method GET",
                string.Empty,
                "## "
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(AiInlineConversationUpdateKind.DocumentChanged, result.UpdateKind);
        Assert.AreEqual(4, result.SuggestedCursorLineNumber);
        Assert.AreEqual(4, result.SuggestedCursorColumn);
    }

    [TestMethod]
    public async Task TryHandleAsync_cleans_chat_from_document_after_agent_applies_edits()
    {
        StubActiveDocumentHost host = new("name \"demo\"\n## tighten this request");
        IAiInlineConversationService service = new AiInlineConversationService(
            new StubTurnExecutor(
                "Done.",
                onExecute: static request =>
                {
                    request.ActiveDocumentHost?.UpdateActiveDocument(
                        request.ActiveDocumentHost.GetActiveDocument()!,
                        "name \"demo\"\nmethod GET\nurl \"https://api.example.test\"");
                }));

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                string.Empty,
                "## ",
                string.Empty,
                "method GET",
                "url \"https://api.example.test\""
            },
            result.UpdatedText.Split('\n'));
        Assert.IsFalse(result.UpdatedText.Contains("#>", StringComparison.Ordinal));
        Assert.IsFalse(result.UpdatedText.Contains("tighten this request", StringComparison.Ordinal));
        Assert.AreEqual(3, result.SuggestedCursorLineNumber);
    }

    [TestMethod]
    public async Task TryHandleAsync_keeps_fresh_prompt_with_the_inline_thread_instead_of_appending_to_document_end()
    {
        IAiInlineConversationService service = new AiInlineConversationService(new StubTurnExecutor("Done."));
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## improve this request",
                "method GET",
                "url \"https://example.test\"",
            ]);
        StubActiveDocumentHost host = new(source);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## improve this request",
                "#> Done.",
                string.Empty,
                "## ",
                string.Empty,
                "method GET",
                "url \"https://example.test\"",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(5, result.SuggestedCursorLineNumber);
    }

    [TestMethod]
    public async Task TryHandleAsync_keeps_response_thread_in_place_when_agent_edits_but_leaves_prompt_present()
    {
        StubActiveDocumentHost host = new("name \"demo\"\n## improve this request\nmethod GET");
        IAiInlineConversationService service = new AiInlineConversationService(
            new StubTurnExecutor(
                "Done.",
                onExecute: static request =>
                {
                    request.ActiveDocumentHost?.UpdateActiveDocument(
                        request.ActiveDocumentHost.GetActiveDocument()!,
                        "name \"demo\"\n## improve this request\nmethod POST");
                }));

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## improve this request",
                "#> Done.",
                string.Empty,
                "## ",
                string.Empty,
                "method POST",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(AiInlineConversationUpdateKind.ResponseOnly, result.UpdateKind);
    }

    [TestMethod]
    public async Task TryHandleAsync_keeps_fresh_prompt_near_the_active_thread_when_older_conversations_exist()
    {
        StubActiveDocumentHost host = new(
            "## old task\n#> old answer\nname \"demo\"\n## improve this request");
        IAiInlineConversationService service = new AiInlineConversationService(
            new StubTurnExecutor(
                "Done.",
                onExecute: static request =>
                {
                    request.ActiveDocumentHost?.UpdateActiveDocument(
                        request.ActiveDocumentHost.GetActiveDocument()!,
                        "name \"demo\"\nmethod GET");
                }));

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 4,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                string.Empty,
                "## ",
                string.Empty,
                "method GET",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(AiInlineConversationUpdateKind.DocumentChanged, result.UpdateKind);
        Assert.AreEqual(3, result.SuggestedCursorLineNumber);
    }

    [TestMethod]
    public async Task TryHandleAsync_keeps_inline_response_when_active_document_source_omits_chat_lines()
    {
        StubActiveDocumentHost host = new("name \"demo\"\nmethod GET");
        IAiInlineConversationService service = new AiInlineConversationService(new StubTurnExecutor("Done."));
        string source = "name \"demo\"\n## improve this request\nmethod GET";

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## improve this request",
                "#> Done.",
                string.Empty,
                "## ",
                string.Empty,
                "method GET",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(AiInlineConversationUpdateKind.ResponseOnly, result.UpdateKind);
    }

    [TestMethod]
    public async Task TryHandleAsync_retries_edit_requests_when_the_agent_only_returns_a_stuck_reply()
    {
        StubActiveDocumentHost host = new("name \"demo\"\n## rewrite this request\nmethod GET");
        StubTurnExecutor executor = new(
            (request, callCount) =>
            {
                if (callCount == 1)
                {
                    return new AiTurnExecutionResult(
                        Succeeded: true,
                        ResponseText: "I can't safely rewrite this yet without knowing which exact syntax you want.",
                        Issues: [],
                        SessionReset: false);
                }

                request.ActiveDocumentHost?.UpdateActiveDocument(
                    request.ActiveDocumentHost.GetActiveDocument()!,
                    "name \"demo\"\nmethod POST");
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Updated the request to POST while keeping it valid.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(2, executor.CallCount);
        StringAssert.Contains(executor.PromptHistory[1], "autonomous repair pass");
        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                string.Empty,
                "## ",
                string.Empty,
                "method POST",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(AiInlineConversationUpdateKind.DocumentChanged, result.UpdateKind);
    }

    [TestMethod]
    public async Task TryHandleAsync_does_not_retry_informational_prompts_that_do_not_need_document_edits()
    {
        StubActiveDocumentHost host = new("name \"demo\"\n## explain this request\nmethod GET");
        StubTurnExecutor executor = new("It sends the current request exactly as written.");
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(1, executor.CallCount);
        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## explain this request",
                "#> It sends the current request exactly as written.",
                string.Empty,
                "## ",
                string.Empty,
                "method GET",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(AiInlineConversationUpdateKind.ResponseOnly, result.UpdateKind);
    }

    [TestMethod]
    public async Task TryHandleAsync_retries_multiple_choice_refusals_for_enumerate_and_stash_edits()
    {
        StubActiveDocumentHost host = new("name \"todo\"\n## Enumerate over 1 through 20, stash completed todos\nmethod GET");
        StubTurnExecutor executor = new(
            (request, callCount) =>
            {
                if (callCount == 1)
                {
                    return new AiTurnExecutionResult(
                        Succeeded: true,
                        ResponseText:
                            "I can\u2019t apply the change yet.\n\n### What I need from you\nAre you trying to:\n1) replace the current request?\n2) create a second request?\nReply \"1\" or \"2\".",
                        Issues: [],
                        SessionReset: false);
                }

                request.ActiveDocumentHost?.UpdateActiveDocument(
                    request.ActiveDocumentHost.GetActiveDocument()!,
                    "name \"todo\"\nmethod GET\nmax_send_iterations 20\n\nforeach todoId in [1..20] {\n  request.url = $\"https://jsonplaceholder.typicode.com/todos/{todoId}\"\n  let sent = request.send()\n  if sent.completed {\n    stash.UserId = sent.userId\n    stash.TodoId = sent.id\n    stash.Title = sent.title\n    stash.Commit()\n  }\n}");
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Updated the active request in place.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-2",
                DocumentTitle: "Todo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(2, executor.CallCount);
        StringAssert.Contains(executor.PromptHistory[1], "autonomous repair pass");
        StringAssert.Contains(executor.PromptHistory[1], "transform the current request in place");
        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.UpdatedText, "max_send_iterations 20");
        StringAssert.Contains(result.UpdatedText, "stash.Title = sent.title");
    }

    private sealed class StubTurnExecutor : IAiTurnExecutor
    {
        private readonly Func<AiTurnExecutionRequest, int, AiTurnExecutionResult> _onExecute;

        public StubTurnExecutor(string responseText = "Done.", Action<AiTurnExecutionRequest>? onExecute = null)
        {
            _onExecute = (request, _) =>
            {
                onExecute?.Invoke(request);
                return new AiTurnExecutionResult(true, responseText, [], SessionReset: false);
            };
        }

        public StubTurnExecutor(Func<AiTurnExecutionRequest, int, AiTurnExecutionResult> onExecute)
        {
            _onExecute = onExecute;
        }

        public int CallCount { get; private set; }

        public List<string> PromptHistory { get; } = [];

        public Task<AiTurnExecutionResult> ExecuteAsync(AiTurnExecutionRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            PromptHistory.Add(request.Prompt);
            return Task.FromResult(_onExecute(request, CallCount));
        }
    }

    private sealed class StubActiveDocumentHost : IAiActiveDocumentHost
    {
        public StubActiveDocumentHost(string sourceText)
        {
            SourceText = sourceText;
        }

        public string SourceText { get; private set; }

        public AiActiveDocumentSnapshot? GetActiveDocument()
        {
            return new("doc-1", "Demo", "forrest", SourceText, []);
        }

        public AiActiveDocumentUpdateResult UpdateActiveDocument(AiActiveDocumentSnapshot document, string updatedText)
        {
            SourceText = updatedText;
            return AiActiveDocumentUpdateResult.Success();
        }
    }
}
