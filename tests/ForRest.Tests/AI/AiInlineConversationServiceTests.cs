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
    public async Task TryHandleAsync_inserts_response_and_fades_older_active_turns()
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
        StringAssert.Contains(result.UpdatedText, "#~ first answer");
        StringAssert.Contains(result.UpdatedText, "#> Applied the fix.");
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
    }

    private sealed class StubTurnExecutor : IAiTurnExecutor
    {
        private readonly string _responseText;

        public StubTurnExecutor(string responseText = "Done.")
        {
            _responseText = responseText;
        }

        public Task<AiTurnExecutionResult> ExecuteAsync(AiTurnExecutionRequest request, CancellationToken cancellationToken = default)
        {
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
            return new("doc-1", "Demo", "forrest", SourceText);
        }

        public AiActiveDocumentUpdateResult UpdateActiveDocument(AiActiveDocumentSnapshot document, string updatedText)
        {
            SourceText = updatedText;
            return AiActiveDocumentUpdateResult.Success();
        }
    }
}
