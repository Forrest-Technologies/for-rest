using System.Text.RegularExpressions;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class MonacoEditorSurfaceSourceTests
{
	[TestMethod]
	public void MonacoEditorSurface_uses_requested_cursor_version_as_the_apply_trigger()
	{
		string source = GetNormalizedMonacoEditorSurfaceSource();
		string lineNumberChangedBody = GetMethodBody(source, "private static void", "OnRequestedCursorLineNumberChanged");
		string columnChangedBody = GetMethodBody(source, "private static void", "OnRequestedCursorColumnChanged");
		string versionChangedBody = GetMethodBody(source, "private static void", "OnRequestedCursorVersionChanged");

		StringAssert.Contains(lineNumberChangedBody, "_pendingRequestedCursorLineNumber");
		Assert.IsFalse(lineNumberChangedBody.Contains("RequestStateApply", StringComparison.Ordinal));
		StringAssert.Contains(columnChangedBody, "_pendingRequestedCursorColumn");
		Assert.IsFalse(columnChangedBody.Contains("RequestStateApply", StringComparison.Ordinal));
		StringAssert.Contains(versionChangedBody, "_pendingRequestedCursorVersion");
		StringAssert.Contains(versionChangedBody, "RequestStateApply");
	}

	[TestMethod]
	public void MonacoEditorSurface_queues_state_applies_instead_of_flushing_them_inline()
	{
		string source = GetNormalizedMonacoEditorSurfaceSource();

		StringAssert.Contains(source, "private int _queuedStateApplyDispatch;");
		StringAssert.Contains(source, "private void QueuePendingStateApply()");
		StringAssert.Contains(source, "private async Task FlushQueuedStateApplyAsync()");
		StringAssert.Contains(source, "Interlocked.Exchange(ref _queuedStateApplyDispatch, 1)");
		StringAssert.Contains(source, "Dispatcher?.Dispatch(DispatchFlush) == true");
		StringAssert.Contains(source, "await Task.Yield();");
		StringAssert.Contains(source, "Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(DispatchFlush);");
	}

	[TestMethod]
	public void MonacoEditorSurface_does_not_force_restore_blank_settings_documents()
	{
		string source = GetNormalizedMonacoEditorSurfaceSource();
		string buildCurrentStatePayloadBody = GetMethodBody(source, "private EditorStatePayload", "BuildCurrentStatePayload");
		string syncEditorTextBody = GetMethodBody(source, "private async Task", "SyncEditorTextAsync");

		Assert.IsFalse(buildCurrentStatePayloadBody.Contains("_lastNonEmptySettingsText", StringComparison.Ordinal));
		Assert.IsFalse(syncEditorTextBody.Contains("_lastNonEmptySettingsText", StringComparison.Ordinal));
		Assert.IsFalse(syncEditorTextBody.Contains("fallbackText", StringComparison.Ordinal));
	}

	[TestMethod]
	public void MonacoEditorSurface_android_webview_registers_native_paste_menu_hooks()
	{
		string source = GetNormalizedMonacoEditorSurfaceSource();

		StringAssert.Contains(source, "_androidPlatformWebView.ContextClickable = true;");
		StringAssert.Contains(source, "_androidPlatformWebView.LongClick += OnAndroidWebViewLongClick;");
		StringAssert.Contains(source, "_androidPlatformWebView.ContextClick += OnAndroidWebViewContextClick;");
		StringAssert.Contains(source, "private async Task ShowAndroidEditorContextMenuAsync()");
		StringAssert.Contains(source, "PopupMenu popupMenu = new(_androidPlatformWebView.Context, _androidPlatformWebView, GravityFlags.Start);");
		StringAssert.Contains(source, "Clipboard.Default.GetTextAsync()");
		StringAssert.Contains(source, "PasteTextFromAndroidContextMenuAsync");
	}

	[TestMethod]
	public void MonacoEditorSurface_exposes_toolbar_clipboard_paste_through_host_bridge()
	{
		string source = GetNormalizedMonacoEditorSurfaceSource();

		StringAssert.Contains(source, "public async Task<bool> PasteFromClipboardAsync()");
		StringAssert.Contains(source, "return await PasteTextFromHostAsync(clipboardText, \"toolbar\");");
		StringAssert.Contains(source, "private async Task<bool> PasteTextFromHostAsync(string? clipboardText, string origin)");
		StringAssert.Contains(source, "window.forRestHost ? (window.forRestHost.pasteTextFromHost(");
	}

	private static string GetMethodBody(string source, string signaturePrefix, string methodName)
	{
		Match match = Regex.Match(
			source,
			$@"{Regex.Escape(signaturePrefix)} {Regex.Escape(methodName)}\([^)]*\)\n\t\{{\n(?<body>.*?)\n\t\}}",
			RegexOptions.Singleline);

		Assert.IsTrue(match.Success, $"{methodName} should exist.");
		return match.Groups["body"].Value;
	}

	private static string GetNormalizedMonacoEditorSurfaceSource()
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
		return File.ReadAllText(sourcePath).Replace("\r\n", "\n", StringComparison.Ordinal);
	}
}
