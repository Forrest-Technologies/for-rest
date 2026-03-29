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

    private sealed class StubTurnExecutor : IAiTurnExecutor
    {
        private readonly string _responseText;
        private readonly Action<AiTurnExecutionRequest>? _onExecute;

        public StubTurnExecutor(string responseText = "Done.", Action<AiTurnExecutionRequest>? onExecute = null)
        {
            _responseText = responseText;
            _onExecute = onExecute;
        }

        public Task<AiTurnExecutionResult> ExecuteAsync(AiTurnExecutionRequest request, CancellationToken cancellationToken = default)
        {
            _onExecute?.Invoke(request);
            return Task.FromResult(new AiTurnExecutionResult(true, _responseText, [], SessionReset: false));
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
