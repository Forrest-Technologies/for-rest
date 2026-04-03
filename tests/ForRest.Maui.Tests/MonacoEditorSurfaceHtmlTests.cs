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

		StringAssert.Contains(html, "sanitizeEditorPosition: function (position) {");
		StringAssert.Contains(html, "this.pendingRenderStabilizationPosition = position ? this.sanitizeEditorPosition(position) : null;");
		StringAssert.Contains(html, "const target = this.sanitizeEditorPosition(this.pendingRenderStabilizationPosition);");
		StringAssert.Contains(html, "scheduleRenderStabilization: function (position) {");
		StringAssert.Contains(html, "this.editor.render(true);");
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
	public void MonacoHostHtml_sanitizes_saved_view_state_before_replacing_editor_text()
	{
		string html = GetMonacoHostHtml();

		StringAssert.Contains(html, "sanitizeViewStateValue: function (value, lines) {");
		StringAssert.Contains(html, "sanitizeEditorViewState: function (viewState, value) {");
		StringAssert.Contains(html, "const viewState = restoreViewState ? this.sanitizeEditorViewState(this.editor.saveViewState(), normalized) : null;");
		StringAssert.Contains(html, "this.editor.restoreViewState(viewState);");
	}

	[TestMethod]
	public void MonacoHostHtml_uses_direct_worker_script_on_android()
	{
		string html = GetMonacoHostHtml();

		StringAssert.Contains(html, "const directWorkerUrl = `${baseUrl}/vs/base/worker/workerMain.js`;");
		StringAssert.Contains(html, "const isAndroidUserAgent = /Android/i.test(navigator.userAgent || \"\");");
		StringAssert.Contains(html, "baseUrl: monacoBaseUrl,");
		StringAssert.Contains(html, "if (isAndroidUserAgent) {");
		StringAssert.Contains(html, "return directWorkerUrl;");
	}

	[TestMethod]
	public void MonacoHostHtml_allows_native_android_context_menu_to_handle_paste()
	{
		string html = GetMonacoHostHtml();

		StringAssert.Contains(html, "contextmenu: !this.isAndroid,");
	}

	[TestMethod]
	public void MonacoHostHtml_accepts_host_driven_paste_for_android_clipboard_menu()
	{
		string html = GetMonacoHostHtml();

		StringAssert.Contains(html, "pasteTextFromHost: function (base64ClipboardText) {");
		StringAssert.Contains(html, "this.editor.focus();");
		StringAssert.Contains(html, "const clipboardText = decodeBase64Utf8(base64ClipboardText);");
		StringAssert.Contains(html, "this.handleInlineAiPromptClipboardPaste(clipboardText, window.monaco)");
		StringAssert.Contains(html, "const position = this.sanitizeEditorPosition(this.editor.getPosition());");
		StringAssert.Contains(html, "const selection = this.editor.getSelection() || (position");
		StringAssert.Contains(html, "this.editor.setSelection(selection);");
		StringAssert.Contains(html, "this.editor.executeEdits(\"forrest-host-paste\", [");
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
