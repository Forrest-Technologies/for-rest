using System.Collections.Generic;
using ForRest.Services.AI;
using ForRest.Scripting;

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
    public async Task TryHandleAsync_handles_help_command_without_invoking_ai_turn_executor()
    {
        StubTurnExecutor executor = new("Should not run.");
        IAiInlineConversationService service = new AiInlineConversationService(executor);
        string source = "name \"demo\"\n## help\nmethod GET";
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

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, executor.CallCount);
        Assert.AreEqual("Listed AI prompt commands.", result.StatusText);
        StringAssert.Contains(result.DebugText, "Command: help");
        StringAssert.Contains(result.ResponseText, "Supported prompt commands:");
        StringAssert.Contains(result.ResponseText, "`reset`");
        StringAssert.Contains(result.ResponseText, "`clear responses`");
        StringAssert.Contains(result.ResponseText, "`collapse`");
        StringAssert.Contains(result.ResponseText, "`help`");
        StringAssert.Contains(result.ResponseText, "`commands`");
        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## help",
                "#> Supported prompt commands:",
                "#> - `reset` clears inline AI prompt and response history and reopens a fresh prompt.",
                "#> - `clear responses` removes inline AI replies and keeps the prompt lines.",
                "#> - `collapse` keeps only the latest inline AI exchange and reopens a fresh prompt.",
                "#> - `help` shows this command list.",
                "#> - `commands` is an alias for `help`.",
                string.Empty,
                "## ",
                string.Empty,
                "method GET",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(AiInlineConversationUpdateKind.ResponseOnly, result.UpdateKind);
        Assert.AreEqual(10, result.SuggestedCursorLineNumber);
        Assert.AreEqual(4, result.SuggestedCursorColumn);
    }

    [TestMethod]
    public async Task TryHandleAsync_handles_clear_responses_command_without_invoking_ai_turn_executor()
    {
        StubTurnExecutor executor = new("Should not run.");
        IAiInlineConversationService service = new AiInlineConversationService(executor);
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## first task",
                "#> first answer",
                "## second task",
                "#~ older answer",
                "method GET",
                "## clear responses",
            ]);
        StubActiveDocumentHost host = new(source);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 7,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, executor.CallCount);
        Assert.AreEqual("Inline AI responses cleared.", result.StatusText);
        StringAssert.Contains(result.DebugText, "Command: clear responses");
        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## first task",
                "## second task",
                "method GET",
                string.Empty,
                "## ",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(AiInlineConversationUpdateKind.ResponseOnly, result.UpdateKind);
        Assert.AreEqual(6, result.SuggestedCursorLineNumber);
        Assert.AreEqual(4, result.SuggestedCursorColumn);
    }

    [TestMethod]
    public async Task TryHandleAsync_handles_collapse_command_without_invoking_ai_turn_executor()
    {
        StubTurnExecutor executor = new("Should not run.");
        IAiInlineConversationService service = new AiInlineConversationService(executor);
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## first task",
                "#> first answer",
                "## latest task",
                "#> latest answer",
                "method GET",
                "## collapse",
            ]);
        StubActiveDocumentHost host = new(source);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 7,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, executor.CallCount);
        Assert.AreEqual("Inline AI history collapsed.", result.StatusText);
        StringAssert.Contains(result.DebugText, "Command: collapse");
        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## latest task",
                "#> latest answer",
                string.Empty,
                "## ",
                string.Empty,
                "method GET",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(AiInlineConversationUpdateKind.ResponseOnly, result.UpdateKind);
        Assert.AreEqual(5, result.SuggestedCursorLineNumber);
        Assert.AreEqual(4, result.SuggestedCursorColumn);
    }

    [TestMethod]
    public async Task TryHandleAsync_executes_multiline_prompt_block_as_a_single_prompt()
    {
        string? capturedPrompt = null;
        StubTurnExecutor executor = new(
            (request, _) =>
            {
                capturedPrompt = request.Prompt;
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Done.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## tighten this request",
                "## add a json body",
                "## ",
                "method GET",
            ]);
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

        Assert.AreEqual("tighten this request\nadd a json body", capturedPrompt);
        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## tighten this request",
                "## add a json body",
                "#> Done.",
                string.Empty,
                "## ",
                string.Empty,
                "method GET",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(6, result.SuggestedCursorLineNumber);
    }

    [TestMethod]
    public async Task TryHandleAsync_compacts_large_api_doc_prompt_before_executor_call()
    {
        string[] promptLines =
        [
            "use this info to make a script that fully tests this api surface area. you should use stashes and dynamically set stuff to test",
            "logs are useful too for showing the actual trace of what you did.",
            "GET",
            "https://api.restful-api.dev/objects",
            "List of all objects",
            "Description",
            "Retrieves a predefined set of sample objects from the public API.",
            "Parameters",
            "id string[] query",
            "Supports multiple values by repeating the parameter in the query string (e.g., ?id=3&id=5&id=10)",
            "Response body example",
            "[",
            "{",
            "\"id\": \"1\",",
            "\"name\": \"Google Pixel 6 Pro\"",
            "}",
            "]",
            "GET",
            "https://api.restful-api.dev/objects/{id}",
            "Single object",
            "POST",
            "https://api.restful-api.dev/objects",
            "Add a new object",
            "Request body example",
            "{",
            "\"name\": \"Apple MacBook Pro 16\"",
            "}",
            "PUT",
            "https://api.restful-api.dev/objects/{id}",
            "Update an object",
            "no need to ask questions, just implement",
        ];
        string rawPrompt = string.Join("\n", promptLines);
        string? capturedPrompt = null;
        string? capturedObjective = null;
        StubTurnExecutor executor = new(
            (request, _) =>
            {
                capturedPrompt = request.Prompt;
                capturedObjective = request.Objective;
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Done.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);
        string source = string.Join(
            "\n",
            [
                "name \"restful-api QA\"",
                "method GET",
                "url \"https://jsonplaceholder.typicode.com/posts/1\"",
                .. promptLines.Select(static line => $"## {line}"),
            ]);
        StubActiveDocumentHost host = new(source);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-large-prompt",
                DocumentTitle: "restful-api QA",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 40,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.IsNotNull(capturedPrompt);
        Assert.IsNotNull(capturedObjective);
        Assert.AreNotEqual(rawPrompt, capturedPrompt);
        StringAssert.Contains(capturedPrompt, "Rewrite the active request in place. Output runnable ForRest source only.");
        StringAssert.Contains(capturedPrompt, "GET https://api.restful-api.dev/objects");
        StringAssert.Contains(capturedPrompt, "POST https://api.restful-api.dev/objects");
        StringAssert.Contains(capturedPrompt, "PUT https://api.restful-api.dev/objects/{id}");
        StringAssert.Contains(capturedPrompt, "Use stash rows to capture the important fields from each step.");
        StringAssert.Contains(capturedPrompt, "Prefer dynamically captured values over hard-coded follow-up ids when possible.");
        StringAssert.Contains(capturedPrompt, "Do not ask follow-up questions unless a real product decision is missing.");
        StringAssert.Contains(capturedPrompt, "Replace the current target `https://jsonplaceholder.typicode.com/posts/1`");
        Assert.IsFalse(capturedPrompt.Contains("Response body example", StringComparison.Ordinal));
        Assert.IsFalse(capturedPrompt.Contains("List of all objects", StringComparison.Ordinal));
        StringAssert.Contains(capturedObjective!, "Prefer zero clarification turns when the request is actionable");
        Assert.IsFalse(capturedObjective.Contains("Prefer dynamic `response.someField` access", StringComparison.Ordinal));
        StringAssert.Contains(result.DebugText, "Prompt compacted: True");
        StringAssert.Contains(result.DebugText, "Effective prompt:");
    }

    [TestMethod]
    public async Task TryHandleAsync_applies_deterministic_restful_api_crud_rewrite_without_waiting_for_ai()
    {
        string[] promptLines =
        [
            "use this info to make a script that fully tests this api surface area. you should use stashes and dynamically set stuff to test",
            "logs are useful too for showing the actual trace of what you did.",
            "GET",
            "https://api.restful-api.dev/objects",
            "List of all objects",
            "Description",
            "Retrieves a predefined set of sample objects from the public API.",
            "Parameters",
            "id string[] query",
            "Supports multiple values by repeating the parameter in the query string (e.g., ?id=3&id=5&id=10)",
            "Response body example",
            "POST",
            "https://api.restful-api.dev/objects",
            "PUT",
            "https://api.restful-api.dev/objects/{id}",
            "PATCH",
            "https://api.restful-api.dev/objects/{id}",
            "DELETE",
            "https://api.restful-api.dev/objects/{id}",
            "no need to ask questions, just implement",
        ];
        string source = string.Join(
            "\n",
            [
                "name \"restful-api QA\"",
                "method GET",
                "url \"https://jsonplaceholder.typicode.com/posts/1\"",
                "timeout 15000",
                "max_send_iterations 3",
                "redirects true",
                "ssl true",
                "history true",
                string.Empty,
                "runtime trace_id = guid()",
                string.Empty,
                "header \"Accept\" = \"application/json\"",
                "header \"X-Workspace\" = \"{{workspace_name}}\"",
                "header \"X-Environment\" = \"{{environment_name}}\"",
                "header \"X-Correlation-Id\" = \"{{trace_id}}\"",
                .. promptLines.Select(static line => $"## {line}"),
            ]);
        StubTurnExecutor executor = new("Should not run.");
        StubActiveDocumentHost host = new(source);
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-deterministic-crud",
                DocumentTitle: "restful-api QA",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 35,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(0, executor.CallCount);
        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Applied built-in request rewrite.", result.StatusText);
        StringAssert.Contains(result.DebugText, "Prompt compacted: True");
        StringAssert.Contains(result.DebugText, "Deterministic rewrite: True");
        StringAssert.Contains(host.SourceText, "url \"https://api.restful-api.dev/objects\"");
        StringAssert.Contains(host.SourceText, "request.method = \"POST\"");
        StringAssert.Contains(host.SourceText, "tests.Equal(3, sent.length(), \"filtered list returns requested ids\")");
        StringAssert.Contains(host.SourceText, "header \"X-Workspace\" = \"{{workspace_name}}\"");
        StringAssert.Contains(host.SourceText, "header \"X-Environment\" = \"{{environment_name}}\"");
        StringAssert.Contains(host.SourceText, "let sample_id_a = convert.ToString(sent[0].id)");
        Assert.IsFalse(host.SourceText.Contains("jsonplaceholder.typicode.com", StringComparison.OrdinalIgnoreCase));

        ForRestScriptCompiler compiler = new(new ForRestScriptParser());
        var compilation = compiler.Compile(
            host.SourceText,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
                DefaultRequestName = "restful-api QA",
            });

        Assert.IsTrue(
            compilation.Succeeded,
            string.Join(Environment.NewLine, compilation.Diagnostics.Select(static diagnostic => $"L{diagnostic.Line}: {diagnostic.Message}")));
    }

    [TestMethod]
    public async Task TryHandleAsync_scans_earlier_prompt_blocks_for_deterministic_restful_api_crud_rewrite_candidates()
    {
        string[] promptLines =
        [
            "use this info to make a script that fully tests this api surface area. you should use stashes and dynamically set stuff to test",
            "logs are useful too for showing the actual trace of what you did.",
            "GET",
            "https://api.restful-api.dev/objects",
            "List of all objects",
            "Description",
            "Retrieves a predefined set of sample objects from the public API.",
            "Parameters",
            "id string[] query",
            "Supports multiple values by repeating the parameter in the query string (e.g., ?id=3&id=5&id=10)",
            "Response body example",
            "POST",
            "https://api.restful-api.dev/objects",
            "PUT",
            "https://api.restful-api.dev/objects/{id}",
            "PATCH",
            "https://api.restful-api.dev/objects/{id}",
            "DELETE",
            "https://api.restful-api.dev/objects/{id}",
            "no need to ask questions, just implement",
        ];
        string source = string.Join(
            "\n",
            [
                "name \"restful-api QA\"",
                "method GET",
                "url \"https://jsonplaceholder.typicode.com/posts/1\"",
                "timeout 15000",
                "max_send_iterations 3",
                "redirects true",
                "ssl true",
                "history true",
                string.Empty,
                "runtime trace_id = guid()",
                string.Empty,
                "header \"Accept\" = \"application/json\"",
                "header \"X-Workspace\" = \"{{workspace_name}}\"",
                "header \"X-Environment\" = \"{{environment_name}}\"",
                "header \"X-Correlation-Id\" = \"{{trace_id}}\"",
                string.Empty,
                .. promptLines.Select(static line => $"## {line}"),
                "#> AI request timed out before completion.",
                string.Empty,
                "## ",
                string.Empty,
                "# ForRest is code-first. request.send() updates response and returns the latest snapshot.",
                "request.headers[\"X-Request-Source\"] = \"maui\"",
                "let attempts = [0..2]",
                "let sent = null",
                string.Empty,
                "foreach attempt in attempts {",
                "  sent = request.send()",
                "  runtime last_attempt = attempt",
                "  if sent.status == 200 and not (sent.body.length() == 0) {",
                "    log $\"Attempt {attempt} returned {sent.status}.\"",
                "    break",
                "  }",
                string.Empty,
                "  warn $\"Attempt {attempt} returned {sent.status}.\"",
                "}",
                string.Empty,
                "expect status == 200 \"returns 200\"",
            ]);
        StubTurnExecutor executor = new("Should not run.");
        StubActiveDocumentHost host = new(source);
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-deterministic-retry-shape",
                DocumentTitle: "restful-api QA",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: source.Split('\n').Length,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(0, executor.CallCount);
        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Applied built-in request rewrite.", result.StatusText);
        StringAssert.Contains(result.DebugText, "Deterministic rewrite candidate line:");
        StringAssert.Contains(result.DebugText, "Attempted request source:");
        StringAssert.Contains(host.SourceText, "url \"https://api.restful-api.dev/objects\"");
        StringAssert.Contains(host.SourceText, "request.method = \"POST\"");
        Assert.IsFalse(host.SourceText.Contains("jsonplaceholder.typicode.com", StringComparison.OrdinalIgnoreCase));

        ForRestScriptCompiler compiler = new(new ForRestScriptParser());
        var compilation = compiler.Compile(
            host.SourceText,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
                DefaultRequestName = "restful-api QA",
            });

        Assert.IsTrue(
            compilation.Succeeded,
            string.Join(Environment.NewLine, compilation.Diagnostics.Select(static diagnostic => $"L{diagnostic.Line}: {diagnostic.Message}")));
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
    public async Task TryHandleAsync_does_not_retry_when_executor_already_used_autonomous_edit_recovery_without_document_changes()
    {
        StubActiveDocumentHost host = new("name \"demo\"\n## rewrite this request\nmethod GET");
        StubTurnExecutor executor = new(
            (_, _) => new AiTurnExecutionResult(
                Succeeded: false,
                ResponseText: "I can't safely rewrite this yet without knowing which exact syntax you want.",
                Issues: [],
                SessionReset: false,
                AutonomousEditRecoveryAttempts: 1));
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-1b",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(1, executor.CallCount);
        Assert.IsFalse(result.Succeeded);
        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## rewrite this request",
                "#> I can't safely rewrite this yet without knowing which exact syntax you want.",
                string.Empty,
                "## ",
                string.Empty,
                "method GET",
            },
            result.UpdatedText.Split('\n'));
        Assert.AreEqual(AiInlineConversationUpdateKind.ResponseOnly, result.UpdateKind);
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

    [TestMethod]
    public async Task TryHandleAsync_retries_when_agent_reports_editor_rejection_and_unchanged_document()
    {
        StubActiveDocumentHost host = new("name \"demo\"\n## make a new request that filters objects and stashes results\nmethod GET");
        StubTurnExecutor executor = new(
            (request, callCount) =>
            {
                if (callCount == 1)
                {
                    return new AiTurnExecutionResult(
                        Succeeded: true,
                        ResponseText:
                            "I tried to replace the active script, but the ForRest editor rejected my update (it reported an invalid `expect` parse). However, your active document is still unchanged and currently contains the old `jsonplaceholder.typicode.com` request.\n\nIf you want, I can retry by patching/replacing again—but I need the active request to be editable successfully.",
                        Issues: [],
                        SessionReset: true);
                }

                request.ActiveDocumentHost?.UpdateActiveDocument(
                    request.ActiveDocumentHost.GetActiveDocument()!,
                    "name \"demo\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"");
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Replaced the request with a valid objects API script.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-3",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(2, executor.CallCount);
        StringAssert.Contains(executor.PromptHistory[1], "autonomous repair pass");
        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.UpdatedText, "https://api.restful-api.dev/objects");
        Assert.AreEqual(AiInlineConversationUpdateKind.DocumentChanged, result.UpdateKind);
    }

    [TestMethod]
    public async Task TryHandleAsync_retries_exact_canvas_rejection_reply_from_user_repro()
    {
        StubActiveDocumentHost host = new(
            "name \"restful-api.dev Test\"\n" +
            "method GET\n" +
            "url \"https://jsonplaceholder.typicode.com/posts/1\"\n" +
            "## Okay, I want you to make a script that hits https://api.restful-api.dev/objects\n" +
            "## Find objects that start with A-C and cost more than 300, then stash up to 50 rows\n");
        StubTurnExecutor executor = new(
            (request, callCount) =>
            {
                if (callCount == 1)
                {
                    return new AiTurnExecutionResult(
                        Succeeded: true,
                        ResponseText:
                            "I can\u2019t apply the requested change in this canvas: the active ForRest script remains unchanged (still `url \"https://jsonplaceholder.typicode.com/posts/1\"`), and any attempted full rewrite to `https://api.restful-api.dev/objects` with the A-C + `price > 300` filter and \u226450-result stash is rejected by the editor as \"not runnable\" due to parse errors (including `; expected` on rewritten `header ...` lines / `expect` placement constraints).",
                        Issues: [],
                        SessionReset: true);
                }

                request.ActiveDocumentHost?.UpdateActiveDocument(
                    request.ActiveDocumentHost.GetActiveDocument()!,
                    "name \"restful-api.dev Test\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"\nmax_send_iterations 50");
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Replaced the request with a working objects API script.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-3a",
                DocumentTitle: "restful-api.dev Test",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 5,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(2, executor.CallCount);
        StringAssert.Contains(executor.PromptHistory[1], "autonomous repair pass");
        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.UpdatedText, "https://api.restful-api.dev/objects");
        StringAssert.Contains(result.UpdatedText, "max_send_iterations 50");
        Assert.AreEqual(AiInlineConversationUpdateKind.DocumentChanged, result.UpdateKind);
    }

    [TestMethod]
    public async Task TryHandleAsync_retries_when_agent_says_the_partial_edit_still_needs_one_more_fix()
    {
        StubActiveDocumentHost host = new("name \"demo\"\n## update this request to filter and stash objects\nmethod GET");
        StubTurnExecutor executor = new(
            (request, callCount) =>
            {
                if (callCount == 1)
                {
                    request.ActiveDocumentHost?.UpdateActiveDocument(
                        request.ActiveDocumentHost.GetActiveDocument()!,
                        "name \"demo\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"\nmax_send_iterations 50");
                    return new AiTurnExecutionResult(
                        Succeeded: true,
                        ResponseText:
                            "I updated the active request to hit `https://api.restful-api.dev/objects`, but the script currently does not run in the editor because the runtime error indicates the parsed JSON object type does not have a `data` field in this environment. If you want, I can fix it next by reading the response JSON structure and adjusting the path.",
                        Issues: [],
                        SessionReset: true);
                }

                request.ActiveDocumentHost?.UpdateActiveDocument(
                    request.ActiveDocumentHost.GetActiveDocument()!,
                    "name \"demo\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"\nmax_send_iterations 50\n\nforeach item in response.body.json().AsArray() {\n  log item[\"name\"]\n}");
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Updated the request to use the actual response shape and keep the filter in place.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-4",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(2, executor.CallCount);
        StringAssert.Contains(executor.PromptHistory[1], "keep repairing it now");
        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.UpdatedText, "response.body.json().AsArray()");
        Assert.AreEqual(AiInlineConversationUpdateKind.DocumentChanged, result.UpdateKind);
    }

    [TestMethod]
    public async Task TryHandleAsync_retries_when_agent_admits_it_did_not_successfully_modify_the_active_document()
    {
        StubActiveDocumentHost host = new("name \"demo\"\n## make this request enumerate 20 todos and stash completed titles\nmethod GET\nurl \"https://jsonplaceholder.typicode.com/todos/1\"");
        StubTurnExecutor executor = new(
            (request, callCount) =>
            {
                if (callCount == 1)
                {
                    return new AiTurnExecutionResult(
                        Succeeded: true,
                        ResponseText:
                            "I attempted to update the active request, but the runtime is currently rejecting the script before scaling it to the full todo flow. I therefore did not successfully modify the active document; it still targets `https://jsonplaceholder.typicode.com/todos/1`. If you want me to keep repairing autonomously, the next step is to switch to the documented foreach/request.send/request.url pattern and then retry the stash logic.",
                        Issues: [],
                        SessionReset: true);
                }

                request.ActiveDocumentHost?.UpdateActiveDocument(
                    request.ActiveDocumentHost.GetActiveDocument()!,
                    "name \"demo\"\nmethod GET\nurl \"https://jsonplaceholder.typicode.com/todos/1\"\nmax_send_iterations 20\n\nforeach todoId in [1..20] {\n  request.url = $\"https://jsonplaceholder.typicode.com/todos/{todoId}\"\n  let sent = request.send()\n  if sent.completed {\n    stash.TodoId = sent.id\n    stash.Title = sent.title\n    stash.Commit()\n  }\n}");
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Updated the request to enumerate todos and stash completed titles.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-4b",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(2, executor.CallCount);
        StringAssert.Contains(executor.PromptHistory[1], "autonomous repair pass");
        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.UpdatedText, "max_send_iterations 20");
        StringAssert.Contains(result.UpdatedText, "stash.Title = sent.title");
        Assert.AreEqual(AiInlineConversationUpdateKind.DocumentChanged, result.UpdateKind);
    }

    [TestMethod]
    public async Task TryHandleAsync_retries_after_timeout_for_repeated_patch_requests_that_left_the_document_unchanged()
    {
        StubActiveDocumentHost host = new("name \"demo\"\n## perform the patch at least 3 times with at least 1 randomized value\nmethod PATCH\nurl \"https://api.restful-api.dev/objects/1\"");
        StubTurnExecutor executor = new(
            (request, callCount) =>
            {
                if (callCount == 1)
                {
                    return new AiTurnExecutionResult(
                        Succeeded: false,
                        ResponseText: "AI request timed out before completion.",
                        Issues: [],
                        SessionReset: true);
                }

                request.ActiveDocumentHost?.UpdateActiveDocument(
                    request.ActiveDocumentHost.GetActiveDocument()!,
                    "name \"demo\"\nmethod PATCH\nurl \"https://api.restful-api.dev/objects/1\"\nmax_send_iterations 3\n\nruntime trace_id = guid()\n\nforeach attempt in [1..3] {\n  request.method = \"PATCH\"\n  request.url = \"https://api.restful-api.dev/objects/1\"\n  request.content_type = \"application/json\"\n  request.body = $\"{{\\\"name\\\":\\\"Patch {attempt} {trace_id}\\\"}}\"\n  let sent = request.send()\n}");
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Updated the request to loop three patch attempts with a unique value.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-4c",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(2, executor.CallCount);
        StringAssert.Contains(executor.PromptHistory[1], "autonomous repair pass");
        StringAssert.Contains(executor.PromptHistory[1], "repeat N times");
        StringAssert.Contains(executor.PromptHistory[1], "Do not invent `Math.*`");
        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.UpdatedText, "foreach attempt in [1..3]");
        Assert.AreEqual(AiInlineConversationUpdateKind.DocumentChanged, result.UpdateKind);
    }

    [TestMethod]
    public async Task TryHandleAsync_retries_user_style_edit_prompts_that_start_with_what_i_want_you_to_do()
    {
        StubActiveDocumentHost host = new(
            "name \"demo\"\n" +
            "## What I want you to do is make a script that hits https://api.restful-api.dev/objects\n" +
            "## Stash the ID, name up to 10 characters, and price for matching objects\n" +
            "method GET");
        StubTurnExecutor executor = new(
            (request, callCount) =>
            {
                if (callCount == 1)
                {
                    return new AiTurnExecutionResult(
                        Succeeded: true,
                        ResponseText:
                            "I updated the active request to hit `https://api.restful-api.dev/objects`, but the script currently does not run in the editor because the runtime error indicates the parsed JSON object type does not have a `data` field in this environment. If you want, I can fix it next by reading the response JSON structure and adjusting the path.",
                        Issues: [],
                        SessionReset: true);
                }

                request.ActiveDocumentHost?.UpdateActiveDocument(
                    request.ActiveDocumentHost.GetActiveDocument()!,
                    "name \"demo\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"\nmax_send_iterations 50");
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Updated the request to use the objects endpoint and keep the filter-ready loop in place.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-5",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 3,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(2, executor.CallCount);
        StringAssert.Contains(executor.PromptHistory[1], "autonomous repair pass");
        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.UpdatedText, "https://api.restful-api.dev/objects");
        Assert.AreEqual(AiInlineConversationUpdateKind.DocumentChanged, result.UpdateKind);
    }

    [TestMethod]
    public async Task TryHandleAsync_uses_compacted_prompt_for_autonomous_repair_passes()
    {
        string source = string.Join(
            "\n",
            [
                "name \"restful-api QA\"",
                "method GET",
                "url \"https://jsonplaceholder.typicode.com/posts/1\"",
                "## use this info to make a script that fully tests this api surface area. you should use stashes and dynamically set stuff to test",
                "## logs are useful too for showing the actual trace of what you did.",
                "## GET",
                "## https://api.restful-api.dev/objects",
                "## Description",
                "## Response body example",
                "## POST",
                "## https://api.restful-api.dev/objects",
                "## PUT",
                "## https://api.restful-api.dev/objects/{id}",
                "## PATCH",
                "## https://api.restful-api.dev/objects/{id}",
            ]);
        StubActiveDocumentHost host = new(source);
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
                    "name \"restful-api QA\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"");
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Updated the active request.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-large-repair",
                DocumentTitle: "restful-api QA",
                Language: "forrest",
                SourceText: source,
                CursorLineNumber: 17,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(2, executor.CallCount);
        Assert.IsFalse(executor.PromptHistory[0].Contains("Response body example", StringComparison.Ordinal));
        Assert.IsFalse(executor.PromptHistory[1].Contains("Response body example", StringComparison.Ordinal));
        StringAssert.Contains(executor.PromptHistory[1], "Original user request: Rewrite the active request in place. Output runnable ForRest source only.");
        StringAssert.Contains(executor.PromptHistory[1], "GET https://api.restful-api.dev/objects");
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public async Task TryHandleAsync_forces_full_replacement_when_agent_says_in_place_patch_still_is_not_updated()
    {
        StubActiveDocumentHost host = new(
            "name \"demo\"\n" +
            "## rewrite this request to use https://api.restful-api.dev/objects and stash matching rows\n" +
            "method GET");
        StubTurnExecutor executor = new(
            (request, callCount) =>
            {
                if (callCount == 1)
                {
                    return new AiTurnExecutionResult(
                        Succeeded: true,
                        ResponseText:
                            "I tried an in-place patch, but the active document is still not updated. Incremental patching safely is too brittle, so I need to replace the entire active document with a safe version. If you allow one action, I can apply that safe version now.",
                        Issues: [],
                        SessionReset: true);
                }

                request.ActiveDocumentHost?.UpdateActiveDocument(
                    request.ActiveDocumentHost.GetActiveDocument()!,
                    "name \"demo\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"\nmax_send_iterations 50");
                return new AiTurnExecutionResult(
                    Succeeded: true,
                    ResponseText: "Replaced the active request with the working objects script.",
                    Issues: [],
                    SessionReset: false);
            });
        IAiInlineConversationService service = new AiInlineConversationService(executor);

        AiInlineConversationResult result = await service.TryHandleAsync(
            new(
                DocumentId: "doc-6",
                DocumentTitle: "Demo",
                Language: "forrest",
                SourceText: host.SourceText,
                CursorLineNumber: 2,
                Settings: new AiSettings { Enabled = true },
                ActiveDocumentHost: host));

        Assert.AreEqual(2, executor.CallCount);
        StringAssert.Contains(executor.PromptHistory[1], "full `replace_active_document` call");
        StringAssert.Contains(executor.PromptHistory[1], "Do not propose a partial logging probe");
        StringAssert.Contains(executor.PromptHistory[1], "replace it now with the corrected request");
        StringAssert.Contains(executor.PromptHistory[1], "documented `request-send`, `request-method`, `request-url`, `request-headers`, `request-body`, `request-content-type`, `api-surface-crud`, `stash`, and top-level `expect` patterns");
        StringAssert.Contains(executor.PromptHistory[1], "strip that prose from the final document");
        StringAssert.Contains(executor.PromptHistory[1], "replace that target instead of leaving the previous URL or method in place");
        Assert.IsTrue(result.Succeeded);
        StringAssert.Contains(result.UpdatedText, "https://api.restful-api.dev/objects");
        Assert.AreEqual(AiInlineConversationUpdateKind.DocumentChanged, result.UpdateKind);
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
