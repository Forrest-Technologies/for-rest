using ForRest.Services.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AiInlineConversationSyntaxTests
{
    [TestMethod]
    public void Parse_classifies_prompt_response_stale_comments_and_text()
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(string.Join(
            "\n",
            [
                "### markdown heading",
                "## ask the ai",
                "#> active answer",
                "#~ stale answer",
                "# ordinary comment",
                "plain text",
            ]));

        Assert.AreEqual(6, document.Lines.Count);
        Assert.AreEqual(AiInlineConversationLineKind.Comment, document.Lines[0].Kind);
        Assert.AreEqual("## markdown heading", document.Lines[0].Content);
        Assert.AreEqual(AiInlineConversationLineKind.Prompt, document.Lines[1].Kind);
        Assert.AreEqual("ask the ai", document.Lines[1].Content);
        Assert.AreEqual(AiInlineConversationLineKind.Response, document.Lines[2].Kind);
        Assert.AreEqual("active answer", document.Lines[2].Content);
        Assert.AreEqual(AiInlineConversationLineKind.StaleResponse, document.Lines[3].Kind);
        Assert.AreEqual("stale answer", document.Lines[3].Content);
        Assert.AreEqual(AiInlineConversationLineKind.Comment, document.Lines[4].Kind);
        Assert.AreEqual("ordinary comment", document.Lines[4].Content);
        Assert.AreEqual(AiInlineConversationLineKind.Text, document.Lines[5].Kind);
        Assert.AreEqual("plain text", document.Lines[5].Content);

        Assert.AreEqual(1, document.Prompts.Count);
        Assert.IsTrue(document.Prompts[0].HasActiveResponse);
        Assert.IsTrue(document.Prompts[0].HasStaleResponse);
    }

    [TestMethod]
    public void FindLatestPrompt_returns_nearest_prompt_before_cursor_line()
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(string.Join(
            "\n",
            [
                "name \"demo\"",
                "## first",
                "#> first answer",
                "# ordinary comment",
                "## second",
                "#> second answer",
            ]));

        Assert.IsNull(document.FindLatestPrompt(1));
        Assert.AreEqual(2, document.FindLatestPrompt(2)?.LineNumber);
        Assert.AreEqual(2, document.FindLatestPrompt(3)?.LineNumber);
        Assert.AreEqual(5, document.FindLatestPrompt(5)?.LineNumber);
        Assert.AreEqual(5, document.FindLatestPrompt(99)?.LineNumber);
    }

    [TestMethod]
    public void Parse_groups_contiguous_prompt_lines_into_a_single_multiline_prompt()
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(string.Join(
            "\n",
            [
                "name \"demo\"",
                "## tighten this request",
                "## add a json body",
                "## ",
                "#> Done.",
                "method GET",
            ]));

        Assert.AreEqual(1, document.Prompts.Count);
        Assert.AreEqual(3, document.Prompts[0].PromptLines.Count);
        Assert.AreEqual("tighten this request\nadd a json body", document.Prompts[0].PromptText);
        Assert.AreEqual(2, document.Prompts[0].LineNumber);
        Assert.IsTrue(document.Prompts[0].HasActiveResponse);
    }

    [TestMethod]
    public void ResolveActionablePrompt_uses_multiline_prompt_text_when_cursor_is_on_blank_submit_line()
    {
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## tighten this request",
                "## add a json body",
                "## ",
                "method GET",
            ]);

        AiInlineConversationPrompt? prompt = AiInlineConversationPromptResolver.ResolveActionablePrompt(source, 4);

        Assert.IsNotNull(prompt);
        Assert.AreEqual("tighten this request\nadd a json body", prompt.PromptText);
        Assert.AreEqual(2, prompt.LineNumber);
    }

    [TestMethod]
    public void ResolveActionablePrompt_resolves_pending_prompt_when_cursor_is_on_blank_line_beneath_it()
    {
        // Mirrors the Android Sora editor: typing `## question` then pressing Enter lands
        // the caret on a plain blank line (no `## ` continuation), with request flow below.
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## do you work??",
                "",
                "",
                "foreach attempt in [0..1] {",
                "}",
            ]);

        AiInlineConversationPrompt? prompt = AiInlineConversationPromptResolver.ResolveActionablePrompt(source, 4);

        Assert.IsNotNull(prompt);
        Assert.AreEqual("do you work??", prompt.PromptText);
        Assert.AreEqual(2, prompt.LineNumber);
    }

    [TestMethod]
    public void ResolveActionablePrompt_ignores_answered_prompt_when_cursor_is_on_blank_line_beneath_it()
    {
        // An already-answered prompt must not hijack a normal send: the nearest non-blank
        // line above the caret is the `#>` response, not the prompt.
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## do you work??",
                "#> yes, I do",
                "",
                "foreach attempt in [0..1] {",
                "}",
            ]);

        AiInlineConversationPrompt? prompt = AiInlineConversationPromptResolver.ResolveActionablePrompt(source, 4);

        Assert.IsNull(prompt);
    }

    [TestMethod]
    public void ResolveActionablePrompt_ignores_blank_cursor_when_nearest_line_above_is_flow_code()
    {
        // Sitting on a blank line under request flow (not under a pending prompt) must
        // execute the request, not route to the AI.
        string source = string.Join(
            "\n",
            [
                "## do you work??",
                "#> yes, I do",
                "method GET",
                "",
            ]);

        AiInlineConversationPrompt? prompt = AiInlineConversationPromptResolver.ResolveActionablePrompt(source, 4);

        Assert.IsNull(prompt);
    }

    [TestMethod]
    public void ResolveActionablePrompt_resolves_pending_prompt_when_cursor_is_on_request_flow_below_blank_gap()
    {
        // Mobile repro: a `## question` typed mid-document with blank lines beneath it and
        // existing request flow further down. Tapping send with the caret resting on the
        // flow lines below the gap must still route to the AI for the unanswered prompt.
        string source = string.Join(
            "\n",
            [
                "name \"Post Echo\"",
                "## make this a browser automation test script",
                "",
                "",
                "",
                "header \"Accept\" = \"application/json\"",
                "header \"X-Workspace\" = \"{{workspace_name}}\"",
            ]);

        AiInlineConversationPrompt? prompt = AiInlineConversationPromptResolver.ResolveActionablePrompt(source, 6);

        Assert.IsNotNull(prompt);
        Assert.AreEqual("make this a browser automation test script", prompt.PromptText);
        Assert.AreEqual(2, prompt.LineNumber);
    }

    [TestMethod]
    public void ResolveActionablePrompt_resolves_pending_prompt_when_cursor_is_on_unprefixed_continuation_text()
    {
        // Mobile repro: the Sora editor does not auto-continue the `## ` marker on Enter, so
        // a user often keeps typing the rest of the prompt on plain (un-prefixed) lines. The
        // unanswered prompt above must still be the actionable target when send is tapped.
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## make this a browser automation test script",
                "that opens the login page",
                "and verifies the title",
                "",
            ]);

        AiInlineConversationPrompt? promptOnText = AiInlineConversationPromptResolver.ResolveActionablePrompt(source, 3);
        AiInlineConversationPrompt? promptOnTrailingBlank = AiInlineConversationPromptResolver.ResolveActionablePrompt(source, 5);

        Assert.IsNotNull(promptOnText);
        Assert.AreEqual(2, promptOnText.LineNumber);
        Assert.IsNotNull(promptOnTrailingBlank);
        Assert.AreEqual(2, promptOnTrailingBlank.LineNumber);
    }

    [TestMethod]
    public void ResolveActionablePrompt_ignores_continuation_text_when_prompt_is_already_answered()
    {
        // The un-prefixed walk-up must still stop at a response line so an answered exchange
        // never hijacks a normal send, even with plain text between the caret and the prompt.
        string source = string.Join(
            "\n",
            [
                "## do you work??",
                "#> yes, I do",
                "extra context line",
                "",
            ]);

        AiInlineConversationPrompt? prompt = AiInlineConversationPromptResolver.ResolveActionablePrompt(source, 4);

        Assert.IsNull(prompt);
    }

    [TestMethod]
    public void RenderResponseBlock_preserves_multiline_structure()
    {
        string rendered = AiInlineConversationFormatter.RenderResponseBlock("first\n\nthird", "\n");

        Assert.AreEqual("#> first\n#>\n#> third", rendered);
    }

    [TestMethod]
    public void ApplyResponseAtCursor_inserts_new_active_response_and_preserves_history()
    {
        string source = string.Join(
            "\n",
            [
                "# ordinary comment",
                "name \"demo\"",
                "## ask the ai",
                "#> old response 1",
                "#> old response 2",
                "#~ faded response",
                "# another comment",
                "method GET",
            ]);

        string updated = AiInlineConversationFormatter.ApplyResponseAtCursor(source, 4, "new response 1\nnew response 2");

        string expected = string.Join(
            "\n",
            [
                "# ordinary comment",
                "name \"demo\"",
                "## ask the ai",
                "#> new response 1",
                "#> new response 2",
                "#~ old response 1",
                "#~ old response 2",
                "#~ faded response",
                "# another comment",
                "method GET",
            ]);

        Assert.AreEqual(expected, updated);
    }

    [TestMethod]
    public void ApplyResponseAtCursor_inserts_after_multiline_prompt_and_removes_blank_submit_line()
    {
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## tighten this request",
                "## add a json body",
                "## ",
                "method GET",
            ]);

        string updated = AiInlineConversationFormatter.ApplyResponseAtCursor(source, 4, "Done.");

        string expected = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## tighten this request",
                "## add a json body",
                "#> Done.",
                "method GET",
            ]);

        Assert.AreEqual(expected, updated);
    }

    [TestMethod]
    public void FadePromptResponses_converts_only_the_target_prompt_block()
    {
        string source = string.Join(
            "\n",
            [
                "## first",
                "#> first active",
                "#~ first stale",
                "# ordinary comment",
                "## second",
                "#> second active",
                "#~ second stale",
            ]);

        string updated = AiInlineConversationFormatter.FadePromptResponses(source, 1);

        string expected = string.Join(
            "\n",
            [
                "## first",
                "#~ first active",
                "#~ first stale",
                "# ordinary comment",
                "## second",
                "#> second active",
                "#~ second stale",
            ]);

        Assert.AreEqual(expected, updated);
    }

    [TestMethod]
    public void ReplaceActiveResponse_swaps_only_the_live_response_block()
    {
        string source = string.Join(
            "\n",
            [
                "## first",
                "#> old active",
                "#~ older answer",
                "method GET",
            ]);

        string updated = AiInlineConversationFormatter.ReplaceActiveResponse(source, 1, "typing");

        string expected = string.Join(
            "\n",
            [
                "## first",
                "#> typing",
                "#~ older answer",
                "method GET",
            ]);

        Assert.AreEqual(expected, updated);
    }

    [TestMethod]
    public void RemoveConversationLines_strips_prompt_and_response_markers_from_request_source()
    {
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## tighten this request",
                "#> Done.",
                "#~ Previous answer",
                "# ordinary comment",
                "method GET",
            ]);

        string updated = AiInlineConversationFormatter.RemoveConversationLines(source);

        string expected = string.Join(
            "\n",
            [
                "name \"demo\"",
                "# ordinary comment",
                "method GET",
            ]);

        Assert.AreEqual(expected, updated);
    }

    [TestMethod]
    public void BlankConversationLines_preserves_line_numbers_for_validation()
    {
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## tighten this request",
                "#> Done.",
                "method GET",
            ]);

        string updated = AiInlineConversationFormatter.BlankConversationLines(source);

        string expected = string.Join(
            "\n",
            [
                "name \"demo\"",
                string.Empty,
                string.Empty,
                "method GET",
            ]);

        Assert.AreEqual(expected, updated);
    }

    [TestMethod]
    public void EnsureFreshPromptAfterConversation_keeps_the_follow_up_prompt_with_the_active_thread()
    {
        string source = string.Join(
            "\n",
            [
                "name \"demo\"",
                "## explain this request",
                "#> Done.",
                "method GET",
            ]);

        string updated = AiInlineConversationFormatter.EnsureFreshPromptAfterConversation(source, 2);

        CollectionAssert.AreEqual(
            new[]
            {
                "name \"demo\"",
                "## explain this request",
                "#> Done.",
                string.Empty,
                "## ",
                string.Empty,
                "method GET",
            },
            updated.Split('\n'));
    }

    [TestMethod]
    public void RemoveConversationLines_keeps_prompt_like_text_inside_triple_quoted_literals()
    {
        string source = string.Join(
            "\n",
            [
                "body json \"\"\"",
                "## keep this heading",
                "#> keep this line too",
                "\"\"\"",
                "## actual prompt",
            ]);

        string updated = AiInlineConversationFormatter.RemoveConversationLines(source);

        string expected = string.Join(
            "\n",
            [
                "body json \"\"\"",
                "## keep this heading",
                "#> keep this line too",
                "\"\"\"",
            ]);

        Assert.AreEqual(expected, updated);
    }
}
