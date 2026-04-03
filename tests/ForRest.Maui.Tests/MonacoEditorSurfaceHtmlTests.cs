using System;
using System.IO;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class MonacoEditorSurfaceHtmlTests
{
	[TestMethod]
	public void MonacoHostHtml_limits_visual_viewport_scroll_relayout_to_android()
	{
		string html = GetMonacoHostHtml();

		StringAssert.Contains(html, "const viewport = this.isAndroid && window.visualViewport ? window.visualViewport : null;");
		StringAssert.Contains(html, "if (this.isAndroid && window.visualViewport) {");
	}

	[TestMethod]
	public void MonacoHostHtml_stabilizes_render_after_host_driven_text_replacements()
	{
		string html = GetMonacoHostHtml();

		StringAssert.Contains(html, "scheduleRenderStabilization: function (position) {");
		StringAssert.Contains(html, "this.editor.render(true);");
		StringAssert.Contains(html, "this.scheduleRenderStabilization();");
		StringAssert.Contains(html, "this.scheduleRenderStabilization(position);");
	}

	[TestMethod]
	public void MonacoHostHtml_skips_restoring_stale_view_state_when_cursor_request_is_pending()
	{
		string html = GetMonacoHostHtml();

		StringAssert.Contains(html, "hasPendingCursorRequest: function () {");
		StringAssert.Contains(html, "const shouldRestoreViewState = !this.hasPendingCursorRequest();");
		StringAssert.Contains(html, "this.replaceEditorValue(normalized, shouldRestoreViewState);");
	}

	[TestMethod]
	public void MonacoHostHtml_normalizes_multiline_inline_ai_paste_before_monaco_inserts_it()
	{
		string html = GetMonacoHostHtml();

		StringAssert.Contains(html, "getLineContentFromValue: function (value, lineNumber) {");
		StringAssert.Contains(html, "buildInlineAiPromptPasteText: function (clipboardText, leadingWhitespace, normalizeFirstLine) {");
		StringAssert.Contains(html, "normalizeInlineAiPromptModelLines: function (startLineNumber, endLineNumber, leadingWhitespace, monaco, normalizeFirstLine) {");
		StringAssert.Contains(html, "const firstLineNumber = normalizeFirstLine ? startLineNumber : startLineNumber + 1;");
		StringAssert.Contains(html, "handleInlineAiPromptClipboardPaste: function (clipboardText, monaco) {");
		StringAssert.Contains(html, "selection.startColumn <= promptPrefix.length");
		StringAssert.Contains(html, "domNode.addEventListener(\"paste\", (event) => {");
		StringAssert.Contains(html, "this.handleInlineAiPromptClipboardPaste(clipboardText, monaco)");
		StringAssert.Contains(html, "const multilineChanges = event.changes.filter((change) =>");
		StringAssert.Contains(html, "const previousStartLineText = this.getLineContentFromValue(this.lastKnownValue, startLineNumber);");
		StringAssert.Contains(html, "const shouldNormalizeFirstLine =");
	}

	private static string GetMonacoHostHtml()
	{
		string sourcePath = Path.GetFullPath(
			Path.Combine(
				AppContext.BaseDirectory,
				"..",
				"..",
				"..",
				"..",
				"..",
				"src",
				"ForRest.Maui",
				"Controls",
				"MonacoEditorSurface.xaml.cs"));
		string source = File.ReadAllText(sourcePath);
		const string marker = "private const string MonacoHostHtml = \"\"\"";
		int startIndex = source.IndexOf(marker, StringComparison.Ordinal);
		Assert.IsTrue(startIndex >= 0, "MonacoHostHtml marker should exist.");
		startIndex += marker.Length;
		int endIndex = source.IndexOf("\"\"\";", startIndex, StringComparison.Ordinal);
		Assert.IsTrue(endIndex > startIndex, "MonacoHostHtml terminator should exist.");
		return source[startIndex..endIndex];
	}
}
