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
}
