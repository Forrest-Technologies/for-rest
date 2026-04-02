using System.Reflection;
using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using ForRest.Maui.ViewModels;
using ForRest.Models;
using ForRest.Services;
using ForRest.Services.AI;
using ForRest.Services.Licensing;
using ForRest.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class MainPageViewModelLayoutTests
{
	[TestMethod]
	public void UpdateLayoutMode_clamps_compact_pane_width_for_narrow_phone_widths()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();

		viewModel.UpdateLayoutMode(280d);

		Assert.IsTrue(viewModel.IsCompactLayout);
		Assert.AreEqual(256d, viewModel.CompactPaneWidth, 0.001d);
		Assert.IsTrue(viewModel.ShowCompactActionBar);
		Assert.IsTrue(viewModel.ShowCompactStatusBar);
		Assert.IsFalse(viewModel.ShowDesktopStatusBar);
	}

	[TestMethod]
	public void SelectExplorerItem_closes_explorer_overlay_in_compact_layout()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		viewModel.UpdateLayoutMode(360d);
		viewModel.ToggleLeftPane();

		NavigationItemViewModel requestItem = viewModel.ExplorerSections
			.SelectMany(section => section.Items)
			.First(item => string.Equals(item.DocumentKind, "request", StringComparison.Ordinal));

		viewModel.SelectExplorerItem(requestItem);

		Assert.IsFalse(viewModel.IsExplorerOverlayVisible);
	}

	[TestMethod]
	public void TogglePane_desktop_collapsed_state_exposes_restore_rails()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();

		viewModel.ToggleLeftPane();
		viewModel.ToggleRightPane();

		Assert.IsTrue(viewModel.ShowLeftPaneRestoreButton);
		Assert.IsTrue(viewModel.ShowRightPaneRestoreButton);
		Assert.AreEqual(72d, viewModel.LeftRestoreRailWidth.Value, 0.001d);
		Assert.AreEqual(72d, viewModel.RightRestoreRailWidth.Value, 0.001d);
		Assert.AreEqual(0d, viewModel.LeftPaneWidth.Value, 0.001d);
		Assert.AreEqual(0d, viewModel.RightPaneWidth.Value, 0.001d);
	}

	[TestMethod]
	public async Task SendAsync_reveals_inspector_overlay_for_compact_layout()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel(new FakeExecutionService());
		viewModel.UpdateLayoutMode(360d);

		await viewModel.SendAsync();

		Assert.IsTrue(viewModel.IsInspectorOverlayVisible);
		Assert.AreEqual("200 OK", viewModel.ResponseState);
	}

	[TestMethod]
	public async Task SendAsync_executes_conversation_free_request_source()
	{
		using TestHarness harness = new();
		FakeExecutionService executionService = new();
		MainPageViewModel viewModel = harness.CreateViewModel(executionService);

		viewModel.ActiveEditorText = """
			name "demo"
			method GET
			url "https://example.test"

			## tighten this request
			#> Done.
			""";
		viewModel.UpdateActiveEditorCursor(1, 1);

		await viewModel.SendAsync();

		Assert.IsNotNull(executionService.LastExecutedSource);
		Assert.IsFalse(executionService.LastExecutedSource.Contains("## tighten this request", StringComparison.Ordinal));
		Assert.IsFalse(executionService.LastExecutedSource.Contains("#> Done.", StringComparison.Ordinal));
		Assert.IsFalse(viewModel.ActiveEditorText.Contains("## tighten this request", StringComparison.Ordinal));
		Assert.IsFalse(viewModel.ActiveEditorText.Contains("#> Done.", StringComparison.Ordinal));
	}

	[TestMethod]
	public async Task SendAsync_populates_stash_rows_from_execution_result()
	{
		using TestHarness harness = new();
		StashTable stash = new()
		{
			Columns = ["Method", "Token"],
			Rows =
			[
				new()
				{
					Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
					{
						["Method"] = "GET",
						["Token"] = "alpha",
					},
				},
				new()
				{
					Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
					{
						["Method"] = "POST",
					},
				},
			],
		};
		MainPageViewModel viewModel = harness.CreateViewModel(new FakeExecutionService(stash));

		await viewModel.SendAsync();

		Assert.IsTrue(viewModel.HasStashData);
		CollectionAssert.AreEqual(new[] { "Method", "Token" }, viewModel.StashColumns.Select(static item => item.Title).ToArray());
		Assert.HasCount(2, viewModel.StashRows);
		CollectionAssert.AreEqual(new[] { "GET", "alpha" }, viewModel.StashRows[0].Cells.Select(static item => item.Value).ToArray());
		CollectionAssert.AreEqual(new[] { "POST", string.Empty }, viewModel.StashRows[1].Cells.Select(static item => item.Value).ToArray());
	}

	[TestMethod]
	public async Task InitializeAsync_recovers_when_request_compile_throws_during_startup()
	{
		using TestHarness harness = new();
		FakeExecutionService executionService = new(throwOnCompile: true);

		MainPageViewModel viewModel = harness.CreateViewModel(executionService);

		Assert.AreEqual(0, executionService.CompileCallCount);
		await viewModel.InitializeAsync();

		Assert.AreEqual(1, executionService.CompileCallCount);
		Assert.AreEqual("Request document unavailable.", viewModel.ExecutionStatus);
		StringAssert.Contains(viewModel.DebugOutputText, "compile boom");
		Assert.AreEqual("Metadata recovery", viewModel.EditorDebugStateText);
	}

	[TestMethod]
	public void RenameSelectedWorkspace_updates_workspace_label_and_rebases_request_locations()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		viewModel.AddWorkspace();

		viewModel.RenameSelectedWorkspace("Ops Lab");

		Assert.AreEqual("Ops Lab", viewModel.SelectedWorkspace);
		Assert.AreEqual("Ops Lab", viewModel.Workspaces.Single(item => item.IsSelected).Title);
		Assert.IsTrue(viewModel.OpenDocuments.All(item => item.Location.Contains("/ops-lab/", StringComparison.OrdinalIgnoreCase)));
		Assert.IsTrue(viewModel.ExplorerSections.SelectMany(section => section.Items).Any(item => item.Context.Contains("/ops-lab/", StringComparison.OrdinalIgnoreCase)));
	}

	[TestMethod]
	public void DeleteSelectedRequest_removes_current_request_and_selects_another_request()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		string deletedRequestName = viewModel.RequestName;

		viewModel.DeleteSelectedRequest();

		Assert.HasCount(2, viewModel.OpenDocuments);
		Assert.AreNotEqual(deletedRequestName, viewModel.RequestName);
		Assert.IsTrue(viewModel.CanDeleteRequest);
	}

	[TestMethod]
	public void DeleteSelectedRequest_replaces_last_request_with_new_request()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();

		while (viewModel.OpenDocuments.Count > 1)
		{
			viewModel.DeleteSelectedRequest();
		}

		viewModel.DeleteSelectedRequest();

		Assert.HasCount(1, viewModel.OpenDocuments);
		Assert.AreEqual("New Request", viewModel.RequestName);
		Assert.IsTrue(viewModel.OpenDocuments.Single().Location.Contains("/requests/", StringComparison.OrdinalIgnoreCase));
	}

	[TestMethod]
	public void DeleteSelectedWorkspace_removes_selected_workspace_and_selects_neighbor()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		viewModel.AddWorkspace();
		viewModel.AddWorkspace();
		string deletedWorkspaceName = viewModel.SelectedWorkspace;

		viewModel.DeleteSelectedWorkspace();

		Assert.HasCount(2, viewModel.Workspaces);
		Assert.AreNotEqual(deletedWorkspaceName, viewModel.SelectedWorkspace);
		Assert.IsTrue(viewModel.CanDeleteWorkspace);
	}

	[TestMethod]
	public void DeleteSelectedWorkspace_replaces_last_workspace_with_new_workspace()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();

		viewModel.DeleteSelectedWorkspace();
		viewModel.DeleteSelectedWorkspace();

		Assert.HasCount(1, viewModel.Workspaces);
		Assert.AreEqual("Workspace 1", viewModel.SelectedWorkspace);
		Assert.AreEqual("Workspace 1", viewModel.Workspaces.Single().Title);
	}

	[TestMethod]
	public void ActiveEditorText_previews_theme_immediately_without_reloading_settings_from_disk()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		NavigationItemViewModel settingsItem = viewModel.ExplorerSections
			.SelectMany(section => section.Items)
			.First(item => string.Equals(item.DocumentKind, "settings", StringComparison.Ordinal));

		viewModel.SelectExplorerItem(settingsItem);
		string updatedText = viewModel.ActiveEditorText
			.Replace("azure = true", "azure = false", StringComparison.Ordinal)
			.Replace("dark = false", "dark = true", StringComparison.Ordinal);

		viewModel.ActiveEditorText = updatedText;

		Assert.AreEqual(new ThemeCatalog().GetTheme(ShellThemeName.Dark).MonacoThemeKey, viewModel.EditorThemeKey);
		StringAssert.Contains(viewModel.ActiveEditorText, "dark = true");
		StringAssert.Contains(viewModel.ActiveEditorText, "azure = false");
	}

	[TestMethod]
	public void ActiveEditorText_previews_style_font_sizes_immediately()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		NavigationItemViewModel settingsItem = viewModel.ExplorerSections
			.SelectMany(section => section.Items)
			.First(item => string.Equals(item.DocumentKind, "settings", StringComparison.Ordinal));

		viewModel.SelectExplorerItem(settingsItem);
		string updatedText = viewModel.ActiveEditorText
			.Replace("editor_font_size = 13.5", "editor_font_size = 16.5", StringComparison.Ordinal)
			.Replace("result_pane_tab_font_size = 11.5", "result_pane_tab_font_size = 13", StringComparison.Ordinal);

		viewModel.ActiveEditorText = updatedText;

		Assert.AreEqual(16.5d, viewModel.ActiveEditorFontSize, 0.001d);
		Assert.AreEqual(13d, viewModel.ResultPaneTabFontSize, 0.001d);
		StringAssert.Contains(viewModel.ActiveEditorText, "editor_font_size = 16.5");
		StringAssert.Contains(viewModel.ActiveEditorText, "result_pane_tab_font_size = 13");
	}

	[TestMethod]
	public void ActiveEditorText_supports_undo_and_redo_for_request_document()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		string originalText = viewModel.ActiveEditorText;
		string updatedText = originalText + "\n# request undo";

		Assert.IsFalse(viewModel.CanUndo);
		Assert.IsFalse(viewModel.CanRedo);

		viewModel.ActiveEditorText = updatedText;

		Assert.AreEqual(updatedText, viewModel.ActiveEditorText);
		Assert.IsTrue(viewModel.CanUndo);
		Assert.IsFalse(viewModel.CanRedo);

		viewModel.Undo();

		Assert.AreEqual(originalText, viewModel.ActiveEditorText);
		Assert.IsFalse(viewModel.CanUndo);
		Assert.IsTrue(viewModel.CanRedo);

		viewModel.Redo();

		Assert.AreEqual(updatedText, viewModel.ActiveEditorText);
		Assert.IsTrue(viewModel.CanUndo);
		Assert.IsFalse(viewModel.CanRedo);
	}

	[TestMethod]
	public void ActiveEditorText_supports_undo_and_redo_for_settings_document()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
		NavigationItemViewModel settingsItem = viewModel.ExplorerSections
			.SelectMany(section => section.Items)
			.First(item => string.Equals(item.DocumentKind, "settings", StringComparison.Ordinal));

		viewModel.SelectExplorerItem(settingsItem);
		string originalText = NormalizeLineEndings(viewModel.ActiveEditorText);
		string updatedText = originalText
			.Replace("azure = true", "azure = false", StringComparison.Ordinal)
			.Replace("dark = false", "dark = true", StringComparison.Ordinal);

		viewModel.ActiveEditorText = updatedText;

		Assert.AreEqual(updatedText, NormalizeLineEndings(viewModel.ActiveEditorText));
		Assert.IsTrue(viewModel.CanUndo);
		Assert.IsFalse(viewModel.CanRedo);

		viewModel.Undo();

		Assert.AreEqual(originalText, NormalizeLineEndings(viewModel.ActiveEditorText));
		Assert.IsFalse(viewModel.CanUndo);
		Assert.IsTrue(viewModel.CanRedo);

		viewModel.Redo();

		Assert.AreEqual(updatedText, NormalizeLineEndings(viewModel.ActiveEditorText));
		Assert.IsTrue(viewModel.CanUndo);
		Assert.IsFalse(viewModel.CanRedo);
	}

	[TestMethod]
	public void Undo_and_redo_keep_request_history_scoped_per_document()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		RequestDocumentViewModel firstDocument = viewModel.OpenDocuments[0];
		string firstOriginalText = viewModel.ActiveEditorText;
		string firstUpdatedText = firstOriginalText + "\n# first document change";

		viewModel.ActiveEditorText = firstUpdatedText;
		viewModel.AddRequest();

		RequestDocumentViewModel secondDocument = viewModel.OpenDocuments[^1];
		string secondOriginalText = viewModel.ActiveEditorText;
		string secondUpdatedText = secondOriginalText + "\n# second document change";

		viewModel.ActiveEditorText = secondUpdatedText;
		viewModel.Undo();

		Assert.AreEqual(secondOriginalText, viewModel.ActiveEditorText);
		Assert.IsTrue(viewModel.CanRedo);

		viewModel.SelectDocument(viewModel.OpenDocuments[0]);

		Assert.AreEqual(firstUpdatedText, viewModel.ActiveEditorText);

		viewModel.Undo();

		Assert.AreEqual(firstOriginalText, viewModel.ActiveEditorText);
		Assert.IsTrue(viewModel.CanRedo);

		viewModel.Redo();

		Assert.AreEqual(firstUpdatedText, viewModel.ActiveEditorText);

		viewModel.SelectDocument(viewModel.OpenDocuments[^1]);
		viewModel.Redo();

		Assert.AreEqual(secondUpdatedText, viewModel.ActiveEditorText);
	}

	[TestMethod]
	public async Task PrepareForShutdownAsync_persists_latest_request_state()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel(new SourceAwareExecutionService());

		viewModel.ActiveEditorText = """
			name "shutdown demo"
			method PUT
			url "https://example.test/shutdown"
			""";

		await viewModel.PrepareForShutdownAsync();

		string stateJson = await File.ReadAllTextAsync(harness.StateFilePath);
		RequestWorkbenchState? state = System.Text.Json.JsonSerializer.Deserialize<RequestWorkbenchState>(
			stateJson,
			new System.Text.Json.JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});

		Assert.IsNotNull(state);
		RequestWorkbenchWorkspaceState workspace = state!.Workspaces.Single(item => item.Id == state.SelectedWorkspaceId);
		RequestWorkbenchDocumentState document = workspace.Documents.Single(
			item => string.Equals(item.Location, workspace.SelectedDocumentLocation, StringComparison.OrdinalIgnoreCase));
		Assert.AreEqual("shutdown demo", document.Title);
		Assert.AreEqual("PUT", document.Method);
		StringAssert.Contains(document.RequestSource, "url \"https://example.test/shutdown\"");
	}

	[TestMethod]
	public async Task PrepareForShutdownAsync_persists_pending_settings_text()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		NavigationItemViewModel settingsItem = viewModel.ExplorerSections
			.SelectMany(section => section.Items)
			.First(item => string.Equals(item.DocumentKind, "settings", StringComparison.Ordinal));

		viewModel.SelectExplorerItem(settingsItem);
		viewModel.ActiveEditorText = viewModel.ActiveEditorText
			.Replace("azure = true", "azure = false", StringComparison.Ordinal)
			.Replace("dark = false", "dark = true", StringComparison.Ordinal);

		await viewModel.PrepareForShutdownAsync();

		string savedConfig = await File.ReadAllTextAsync(harness.ConfigFilePath);
		StringAssert.Contains(savedConfig, "azure = false");
		StringAssert.Contains(savedConfig, "dark = true");
	}

	[TestMethod]
	public async Task ActiveEditorText_refreshes_settings_projection_after_ai_toggle_autosave()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel();
		NavigationItemViewModel settingsItem = viewModel.ExplorerSections
			.SelectMany(section => section.Items)
			.First(item => string.Equals(item.DocumentKind, "settings", StringComparison.Ordinal));

		viewModel.SelectExplorerItem(settingsItem);
		Assert.IsFalse(viewModel.ActiveEditorText.Contains("provider = ", StringComparison.Ordinal));
		string updatedText = viewModel.ActiveEditorText.Replace("enabled = false", "enabled = true", StringComparison.Ordinal);

		viewModel.ActiveEditorText = updatedText;
		await Task.Delay(900);

		StringAssert.Contains(viewModel.ActiveEditorText, "enabled = true");
		StringAssert.Contains(viewModel.ActiveEditorText, "provider = \"openai\"");
		StringAssert.Contains(viewModel.ActiveEditorText, "api_key = \"\"");
	}

	[TestMethod]
	public async Task SendAsync_routes_active_inline_ai_prompt_to_ai_service()
	{
		using TestHarness harness = new();
		FakeAiInlineConversationService aiService = new(
			new AiInlineConversationResult(
				Handled: true,
				Succeeded: true,
				UpdatedText: "name \"demo\"\n## tighten this request\n#> Done.",
				StatusText: "AI replied.",
				DebugText: "ai debug"));
		FakeExecutionService executionService = new();
		MainPageViewModel viewModel = harness.CreateViewModel(executionService, aiService);

		viewModel.ActiveEditorText = "name \"demo\"\n## tighten this request";
		viewModel.UpdateActiveEditorCursor(2, 3);

		await viewModel.SendAsync();

		Assert.AreEqual(1, aiService.CallCount);
		Assert.AreEqual(0, executionService.ExecuteCallCount);
		StringAssert.Contains(viewModel.ActiveEditorText, "#> Done.");
		Assert.AreEqual("AI replied.", viewModel.ExecutionStatus);
	}

	[TestMethod]
	public async Task TryHandleInlineAiAsync_timeout_debug_output_includes_prompt_and_attempted_request_source()
	{
		using TestHarness harness = new();
		FakeAiInlineConversationService aiService = new(
			AiInlineConversationResult.NotHandled(string.Empty),
			tryHandleAsync: async (_, cancellationToken) =>
			{
				await Task.Delay(Timeout.Infinite, cancellationToken);
				return AiInlineConversationResult.NotHandled(string.Empty);
			});
		MainPageViewModel viewModel = harness.CreateViewModel(new FakeExecutionService(), aiService);
		MethodInfo method = typeof(MainPageViewModel).GetMethod(
			"TryHandleInlineAiAsync",
			BindingFlags.Instance | BindingFlags.NonPublic)!;

		viewModel.ActiveEditorText =
			"""
			name "demo"
			method GET
			url "https://example.test"

			## fix this request
			""";
		viewModel.UpdateActiveEditorCursor(5, 4);

		Task<bool> task = (Task<bool>)method.Invoke(
			viewModel,
			[
				new AiSettings
				{
					Enabled = true,
					Conversation = new AiConversationSettings
					{
						ExecutionTimeoutSeconds = 1,
					},
				},
			])!;

		bool handled = await task;

		Assert.IsTrue(handled);
		Assert.AreEqual("AI request timed out.", viewModel.ExecutionStatus);
		StringAssert.Contains(viewModel.DebugOutputText, "The AI request exceeded the configured 1-second timeout.");
		StringAssert.Contains(viewModel.DebugOutputText, "Resolved inline AI prompt line: 5");
		StringAssert.Contains(viewModel.DebugOutputText, "Resolved inline AI prompt:");
		StringAssert.Contains(viewModel.DebugOutputText, "fix this request");
		StringAssert.Contains(viewModel.DebugOutputText, "Attempted request source:");
		StringAssert.Contains(viewModel.DebugOutputText, "name \"demo\"");
		StringAssert.Contains(viewModel.DebugOutputText, "url \"https://example.test\"");
	}

	[TestMethod]
	public async Task SendAsync_handles_reset_inline_ai_command_without_invoking_turn_executor()
	{
		using TestHarness harness = new();
		IAiInlineConversationService aiService = new AiInlineConversationService(new ThrowingTurnExecutor());
		FakeExecutionService executionService = new();
		MainPageViewModel viewModel = harness.CreateViewModel(executionService, aiService);

		viewModel.ActiveEditorText = """
			name "demo"
			## first task
			#> first answer
			method GET
			## reset
			""";
		viewModel.UpdateActiveEditorCursor(5, 4);

		await viewModel.SendAsync();

		Assert.AreEqual(0, executionService.ExecuteCallCount);
		Assert.AreEqual("AI history reset.", viewModel.ExecutionStatus);
		Assert.IsFalse(viewModel.ActiveEditorText.Contains("## first task", StringComparison.Ordinal));
		Assert.IsFalse(viewModel.ActiveEditorText.Contains("#> first answer", StringComparison.Ordinal));
		CollectionAssert.AreEqual(
			new[]
			{
				"name \"demo\"",
				"method GET",
				string.Empty,
				"## "
			},
			viewModel.ActiveEditorText.Split('\n'));
	}

	[TestMethod]
	public async Task SendAsync_reset_inline_ai_command_can_be_undone_and_redone()
	{
		using TestHarness harness = new();
		IAiInlineConversationService aiService = new AiInlineConversationService(new ThrowingTurnExecutor());
		MainPageViewModel viewModel = harness.CreateViewModel(new FakeExecutionService(), aiService);
		string originalText = NormalizeLineEndings(
			"""
			name "demo"
			## first task
			#> first answer
			method GET
			## reset
			""");

		viewModel.ActiveEditorText = originalText;
		viewModel.UpdateActiveEditorCursor(5, 4);

		await viewModel.SendAsync();

		string resetText = NormalizeLineEndings(viewModel.ActiveEditorText);
		Assert.IsTrue(viewModel.CanUndo);

		viewModel.Undo();

		Assert.AreEqual(originalText, NormalizeLineEndings(viewModel.ActiveEditorText));
		Assert.IsTrue(viewModel.CanRedo);

		viewModel.Redo();

		Assert.AreEqual(resetText, NormalizeLineEndings(viewModel.ActiveEditorText));
	}

	[TestMethod]
	public async Task SendAsync_shows_transient_working_line_while_inline_ai_request_is_running()
	{
		using TestHarness harness = new();
		TaskCompletionSource<AiInlineConversationResult> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeAiInlineConversationService aiService = new(
			AiInlineConversationResult.NotHandled(string.Empty),
			tryHandleAsync: (_, cancellationToken) => gate.Task.WaitAsync(cancellationToken));
		MainPageViewModel viewModel = harness.CreateViewModel(new FakeExecutionService(), aiService);

		viewModel.ActiveEditorText = "name \"demo\"\n## tighten this request";
		viewModel.UpdateActiveEditorCursor(2, 4);

		Task sendTask = viewModel.SendAsync();
		for (int attempt = 0;
		     attempt < 10 && !viewModel.ActiveEditorText.Contains("#> Working", StringComparison.Ordinal);
		     attempt++)
		{
			await Task.Delay(40);
		}

		StringAssert.Contains(viewModel.ActiveEditorText, "#> Working");

		gate.SetResult(
			new AiInlineConversationResult(
				Handled: true,
				Succeeded: true,
				UpdatedText: "name \"demo\"\n## tighten this request\n#> Done.",
				StatusText: "AI replied.",
				DebugText: "ai debug",
				ResponseText: "Done.",
				PromptLineNumber: 2,
				UpdateKind: AiInlineConversationUpdateKind.ResponseOnly));
		await sendTask;

		StringAssert.Contains(viewModel.ActiveEditorText, "#> Done.");
	}

	[TestMethod]
	public async Task SendAsync_routes_trailing_inline_ai_prompt_after_blank_line_to_ai_service()
	{
		using TestHarness harness = new();
		FakeAiInlineConversationService aiService = new(
			new AiInlineConversationResult(
				Handled: true,
				Succeeded: true,
				UpdatedText: "name \"demo\"\n## Do you work agent?\n#> Yes.",
				StatusText: "AI replied.",
				DebugText: "ai debug"));
		FakeExecutionService executionService = new();
		MainPageViewModel viewModel = harness.CreateViewModel(executionService, aiService);

		viewModel.ActiveEditorText = "name \"demo\"\n## Do you work agent?\n";
		viewModel.UpdateActiveEditorCursor(3, 1);

		await viewModel.SendAsync();

		Assert.AreEqual(1, aiService.CallCount);
		Assert.AreEqual(0, executionService.ExecuteCallCount);
		StringAssert.Contains(viewModel.ActiveEditorText, "#> Yes.");
		Assert.AreEqual("AI replied.", viewModel.ExecutionStatus);
	}

	[TestMethod]
	public async Task SendAsync_routes_multiline_inline_ai_prompt_block_to_ai_service()
	{
		using TestHarness harness = new();
		CapturingTurnExecutor executor = new("Done.");
		IAiInlineConversationService aiService = new AiInlineConversationService(executor);
		MainPageViewModel viewModel = harness.CreateViewModel(new FakeExecutionService(), aiService);

		viewModel.ActiveEditorText =
			"""
			name "demo"
			## tighten this request
			## add a json body
			## 
			method GET
			""";
		viewModel.UpdateActiveEditorCursor(4, 4);

		await viewModel.SendAsync();

		Assert.AreEqual("tighten this request\nadd a json body", executor.LastPrompt);
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
			viewModel.ActiveEditorText.Split('\n'));
		Assert.AreEqual("AI replied.", viewModel.ExecutionStatus);
		Assert.AreEqual(6, viewModel.ActiveEditorRequestedCursorLineNumber);
	}

	[TestMethod]
	public async Task SendAsync_requests_follow_up_cursor_move_after_inline_ai_reply()
	{
		using TestHarness harness = new();
		FakeAiInlineConversationService aiService = new(
			new AiInlineConversationResult(
				Handled: true,
				Succeeded: true,
				UpdatedText: "name \"demo\"\n## tighten this request\n#> Done.\n## \nmethod GET",
				StatusText: "AI replied.",
				DebugText: "ai debug",
				SuggestedCursorLineNumber: 4,
				SuggestedCursorColumn: 4));
		MainPageViewModel viewModel = harness.CreateViewModel(new FakeExecutionService(), aiService);

		viewModel.ActiveEditorText = "name \"demo\"\n## tighten this request\nmethod GET";
		viewModel.UpdateActiveEditorCursor(2, 4);

		await viewModel.SendAsync();

		Assert.AreEqual(4, viewModel.ActiveEditorRequestedCursorLineNumber);
		Assert.AreEqual(4, viewModel.ActiveEditorRequestedCursorColumn);
		Assert.AreEqual(1, viewModel.ActiveEditorRequestedCursorVersion);
	}

	[TestMethod]
	public async Task SendAsync_does_not_queue_a_second_legacy_cursor_move_after_inline_ai_reply()
	{
		using TestHarness harness = new();
		FakeAiInlineConversationService aiService = new(
			new AiInlineConversationResult(
				Handled: true,
				Succeeded: true,
				UpdatedText: "name \"demo\"\n## tighten this request\n#> Done.\n## \nmethod GET",
				StatusText: "AI replied.",
				DebugText: "ai debug",
				SuggestedCursorLineNumber: 4,
				SuggestedCursorColumn: 4));
		MainPageViewModel viewModel = harness.CreateViewModel(new FakeExecutionService(), aiService);

		viewModel.ActiveEditorText = "name \"demo\"\n## tighten this request\nmethod GET";
		viewModel.UpdateActiveEditorCursor(2, 4);

		await viewModel.SendAsync();

		Assert.IsFalse(viewModel.TryConsumePendingEditorCursorRequest(out _, out _));
		Assert.AreEqual(4, viewModel.ActiveEditorRequestedCursorLineNumber);
		Assert.AreEqual(4, viewModel.ActiveEditorRequestedCursorColumn);
	}

	[TestMethod]
	public async Task SendAsync_repairs_missing_follow_up_prompt_after_ai_reply()
	{
		using TestHarness harness = new();
		FakeAiInlineConversationService aiService = new(
			new AiInlineConversationResult(
				Handled: true,
				Succeeded: true,
				UpdatedText:
				"""
				name "demo"
				## explain this request
				#> This reply used to stop without a fresh prompt.
				method GET
				""",
				StatusText: "AI replied.",
				DebugText: "ai debug",
				ResponseText: "This reply used to stop without a fresh prompt.",
				PromptLineNumber: 2,
				UpdateKind: AiInlineConversationUpdateKind.ResponseOnly));
		MainPageViewModel viewModel = harness.CreateViewModel(new FakeExecutionService(), aiService);

		viewModel.ActiveEditorText = "name \"demo\"\n## explain this request\nmethod GET";
		viewModel.UpdateActiveEditorCursor(2, 4);

		await viewModel.SendAsync();

		CollectionAssert.AreEqual(
			new[]
			{
				"name \"demo\"",
				"## explain this request",
				"#> This reply used to stop without a fresh prompt.",
				string.Empty,
				"## ",
				string.Empty,
				"method GET",
			},
			viewModel.ActiveEditorText.Split('\n'));
		Assert.AreEqual(5, viewModel.ActiveEditorRequestedCursorLineNumber);
		Assert.AreEqual(4, viewModel.ActiveEditorRequestedCursorColumn);
	}

	[TestMethod]
	public async Task SendAsync_streaming_reply_preserves_follow_up_prompt_after_ai_reply()
	{
		using TestHarness harness = new();
		File.WriteAllText(
			harness.ConfigFilePath,
			"""
			license = ""

			[appearance.theme]
			light = true
			azure = false
			dark = false
			black = false
			amber = false

			[ai]
			enabled = true
			stream_responses = true
			provider = "openai"
			api = "responses"
			model = "gpt-4.1-mini"
			api_key = "workbench-api-key"
			system_prompt = "Use terse answers."
			""");
		FakeAiInlineConversationService aiService = new(
			new AiInlineConversationResult(
				Handled: true,
				Succeeded: true,
				UpdatedText:
				"""
				name "demo"
				## explain this request
				#> This reply used to stop without a fresh prompt.
				method GET
				""",
				StatusText: "AI replied.",
				DebugText: "ai debug",
				ResponseText: "This reply used to stop without a fresh prompt.",
				PromptLineNumber: 2,
				UpdateKind: AiInlineConversationUpdateKind.ResponseOnly));
		MainPageViewModel viewModel = harness.CreateViewModel(new FakeExecutionService(), aiService);

		viewModel.ActiveEditorText = "name \"demo\"\n## explain this request\nmethod GET";
		viewModel.UpdateActiveEditorCursor(2, 4);

		await viewModel.SendAsync();

		CollectionAssert.AreEqual(
			new[]
			{
				"name \"demo\"",
				"## explain this request",
				"#> This reply used to stop without a fresh prompt.",
				string.Empty,
				"## ",
				string.Empty,
				"method GET",
			},
			viewModel.ActiveEditorText.Split('\n'));
		Assert.AreEqual(5, viewModel.ActiveEditorRequestedCursorLineNumber);
		Assert.AreEqual(4, viewModel.ActiveEditorRequestedCursorColumn);
	}

	[TestMethod]
	public async Task SendAsync_keeps_regular_request_send_when_cursor_is_not_on_ai_prompt()
	{
		using TestHarness harness = new();
		FakeAiInlineConversationService aiService = new(AiInlineConversationResult.NotHandled(string.Empty));
		FakeExecutionService executionService = new();
		MainPageViewModel viewModel = harness.CreateViewModel(executionService, aiService);

		viewModel.ActiveEditorText = "## old ai note\nname \"demo\"\nmethod GET";
		viewModel.UpdateActiveEditorCursor(3, 1);

		await viewModel.SendAsync();

		Assert.AreEqual(1, aiService.CallCount);
		Assert.AreEqual(1, executionService.ExecuteCallCount);
		Assert.AreEqual("200 OK", viewModel.ResponseState);
	}

	[TestMethod]
	public async Task SendAsync_keeps_regular_request_send_when_trailing_ai_prompt_is_blank()
	{
		using TestHarness harness = new();
		FakeExecutionService executionService = new();
		MainPageViewModel viewModel = harness.CreateViewModel(
			executionService,
			new AiInlineConversationService(new ThrowingTurnExecutor()));

		viewModel.ActiveEditorText = "name \"demo\"\nmethod GET\n\n## ";
		viewModel.UpdateActiveEditorCursor(4, 4);

		await viewModel.SendAsync();

		Assert.AreEqual(1, executionService.ExecuteCallCount);
		Assert.AreEqual("200 OK", viewModel.ResponseState);
	}

	[TestMethod]
	public async Task SendAsync_rebases_request_location_when_ai_rewrites_request_identity()
	{
		using TestHarness harness = new();
		FakeAiInlineConversationService aiService = new(
			new AiInlineConversationResult(
				Handled: true,
				Succeeded: true,
				UpdatedText:
				"""
				name "Post Echo"
				method POST
				url "https://httpbin.org/headers"
				timeout 15000
				max_send_iterations 3
				redirects true
				ssl true
				history true

				runtime trace_id = guid()

				header "Accept" = "application/json"
				header "X-Workspace" = "{{workspace_name}}"
				header "X-Environment" = "{{environment_name}}"
				header "X-Correlation-Id" = "{{trace_id}}"

				request.send()

				expect status == 200 "returns 200"
				expect header "Content-Type" contains "json" "json response"
				""",
				StatusText: "AI replied.",
				DebugText: "ai debug"));
		MainPageViewModel viewModel = harness.CreateViewModel(
			executionService: new SourceAwareExecutionService(),
			aiInlineConversationService: aiService);
		NavigationItemViewModel getUuidItem = viewModel.ExplorerSections
			.SelectMany(section => section.Items)
			.First(item => string.Equals(item.Title, "Get UUID", StringComparison.Ordinal));

		viewModel.SelectExplorerItem(getUuidItem);
		viewModel.ActiveEditorText = $"{viewModel.ActiveEditorText}\n\n## rewrite this script from scratch";
		viewModel.UpdateActiveEditorCursor(viewModel.ActiveEditorText.Split('\n').Length, 4);

		await viewModel.SendAsync();

		StringAssert.Contains(viewModel.RequestLocation, "/post-echo");
		Assert.IsFalse(viewModel.RequestLocation.Contains("get-uuid", StringComparison.OrdinalIgnoreCase));
		Assert.AreEqual("POST httpbin.org/headers", viewModel.RequestSummary);
		NavigationItemViewModel selectedItem = viewModel.ExplorerSections
			.SelectMany(section => section.Items)
			.Single(item => item.IsSelected);
		StringAssert.Contains(selectedItem.Context, "/post-echo");
		Assert.AreEqual("Post Echo", selectedItem.Title);
	}

	[TestMethod]
	public async Task SendAsync_exposes_script_validation_diagnostics_to_inline_ai()
	{
		using TestHarness harness = new();
		AiActiveDocumentSnapshot? capturedDocument = null;
		FakeAiInlineConversationService aiService = new(
			new AiInlineConversationResult(
				Handled: true,
				Succeeded: false,
				UpdatedText: "name \"demo\"\n## fix this",
				StatusText: "AI could not complete the request.",
				DebugText: "ai debug"),
			onTryHandle: request => capturedDocument = request.ActiveDocumentHost.GetActiveDocument());
		MainPageViewModel viewModel = harness.CreateViewModel(
			executionService: new ValidationAwareExecutionService(),
			aiInlineConversationService: aiService,
			scriptEngine: new FailingScriptEngine("(35,20): error CS1002: ; expected"));

		viewModel.ActiveEditorText = "name \"demo\"\nmethod GET\nurl \"https://example.test\"\n\n## fix this";
		viewModel.UpdateActiveEditorCursor(5, 4);

		await viewModel.SendAsync();

		Assert.IsNotNull(capturedDocument);
		Assert.AreEqual("name \"demo\"\nmethod GET\nurl \"https://example.test\"\n", capturedDocument.SourceText);
		Assert.IsTrue(capturedDocument.Diagnostics.Any(static diagnostic => diagnostic.Line == 35 && diagnostic.Message.Contains("; expected", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void ActiveRequestDocumentHost_reuses_cached_diagnostics_for_repeated_reads()
	{
		using TestHarness harness = new();
		ValidationAwareExecutionService executionService = new();
		MainPageViewModel viewModel = harness.CreateViewModel(
			executionService: executionService,
			scriptEngine: new FakeScriptEngine());
		viewModel.ActiveEditorText = "name \"demo\"\nmethod GET\nurl \"https://example.test\"\n\n## fix this";

		IAiActiveDocumentHost host = CreateActiveRequestDocumentHost(viewModel, viewModel.ActiveEditorText);
		int baselineCompileCount = executionService.CompileCallCount;

		AiActiveDocumentSnapshot? first = host.GetActiveDocument();
		AiActiveDocumentSnapshot? second = host.GetActiveDocument();

		Assert.IsNotNull(first);
		Assert.IsNotNull(second);
		Assert.AreEqual(baselineCompileCount + 1, executionService.CompileCallCount);
	}

	[TestMethod]
	public void ActiveRequestDocumentHost_reuses_compilation_results_for_rejected_script_validation_updates()
	{
		using TestHarness harness = new();
		ValidationAwareExecutionService executionService = new();
		MainPageViewModel viewModel = harness.CreateViewModel(
			executionService: executionService,
			scriptEngine: new FailingScriptEngine("(35,20): error CS1002: ; expected"));
		viewModel.ActiveEditorText = "name \"demo\"\nmethod GET\nurl \"https://example.test\"\n\n## fix this";

		IAiActiveDocumentHost host = CreateActiveRequestDocumentHost(viewModel, viewModel.ActiveEditorText);
		AiActiveDocumentSnapshot document = host.GetActiveDocument()!;
		int baselineCompileCount = executionService.CompileCallCount;

		AiActiveDocumentUpdateResult result = host.UpdateActiveDocument(
			document,
			"name \"demo\"\nmethod GET\nurl \"https://example.test\"\n");

		Assert.IsFalse(result.Succeeded);
		Assert.AreEqual(baselineCompileCount + 1, executionService.CompileCallCount);
		Assert.IsTrue(result.Diagnostics.Any(static diagnostic => diagnostic.Line == 35 && diagnostic.Message.Contains("; expected", StringComparison.Ordinal)));
		StringAssert.Contains(result.Message, "generated scripts do not compile");
	}

	[TestMethod]
	public async Task SendAsync_exposes_latest_runtime_failure_to_inline_ai()
	{
		using TestHarness harness = new();
		AiActiveDocumentSnapshot? capturedDocument = null;
		FakeAiInlineConversationService aiService = new(
			new AiInlineConversationResult(
				Handled: false,
				Succeeded: false,
				UpdatedText: string.Empty,
				StatusText: "AI could not complete the request.",
				DebugText: "ai debug"),
			onTryHandle: request =>
			{
				if (request.SourceText.Contains("## fix this", StringComparison.Ordinal))
				{
					capturedDocument = request.ActiveDocumentHost.GetActiveDocument();
				}
			},
			tryHandleAsync: (request, _) => Task.FromResult(
				request.SourceText.Contains("## fix this", StringComparison.Ordinal)
					? new AiInlineConversationResult(
						Handled: true,
						Succeeded: false,
						UpdatedText: string.Empty,
						StatusText: "AI could not complete the request.",
						DebugText: "ai debug")
					: new AiInlineConversationResult(
						Handled: false,
						Succeeded: false,
						UpdatedText: request.SourceText,
						StatusText: string.Empty,
						DebugText: string.Empty)));
		MainPageViewModel viewModel = harness.CreateViewModel(
			executionService: new RuntimeFailureExecutionService(),
			aiInlineConversationService: aiService,
			scriptEngine: new FakeScriptEngine());

		viewModel.ActiveEditorText = "name \"demo\"\nmethod GET\nurl \"https://api.restful-api.dev/objects\"\n";
		await viewModel.SendAsync();

		viewModel.ActiveEditorText = $"{NormalizeLineEndings(viewModel.ActiveEditorText)}\n## fix this";
		viewModel.UpdateActiveEditorCursor(viewModel.ActiveEditorText.Split('\n').Length, 4);

		await viewModel.SendAsync();

		Assert.IsNotNull(capturedDocument);
		Assert.IsNotNull(capturedDocument.RuntimeContext);
		Assert.AreEqual("Failed", capturedDocument.RuntimeContext.Status);
		StringAssert.Contains(capturedDocument.RuntimeContext.ErrorMessage, "Cannot perform runtime binding on a null reference");
		StringAssert.Contains(capturedDocument.RuntimeContext.DebugText, "Execution state: Failed");
		StringAssert.Contains(capturedDocument.RuntimeContext.ResponseBodyPreview, "\"data\": null");
	}

	[TestMethod]
	public void TryValidateCompiledRequestScripts_uses_compile_only_validation_without_executing_scripts()
	{
		using TestHarness harness = new();
		ArrayRootValidationExecutionService executionService = new();
		ArrayRootValidationScriptEngine scriptEngine = new();
		MainPageViewModel viewModel = harness.CreateViewModel(
			executionService: executionService,
			scriptEngine: scriptEngine);

		ForRestScriptCompilationResult compilation = executionService.Compile(
			"""
			name "demo"
			method GET
			url "https://api.restful-api.dev/objects"
			""",
			Guid.NewGuid(),
			"Demo");
		Assert.IsNotNull(compilation.Payload);

		MethodInfo method = typeof(MainPageViewModel).GetMethod(
			"TryValidateCompiledRequestScripts",
			BindingFlags.Instance | BindingFlags.NonPublic)!;
		object?[] arguments = [compilation.Payload, null];

		bool succeeded = (bool)method.Invoke(viewModel, arguments)!;

		Assert.IsTrue(succeeded);
		Assert.AreEqual(string.Empty, arguments[1] as string);
		Assert.AreEqual(0, scriptEngine.RunCallCount);
	}

	[TestMethod]
	public void TryValidateCompiledRequestScripts_accepts_restful_api_crud_flows_with_request_mutations()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel(
			scriptEngine: new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance));
		ForRestScriptCompiler compiler = new(new ForRestScriptParser());

		ForRestScriptCompilationResult compilation = compiler.Compile(
			"""
			name "restful-api QA"
			method GET
			url "https://api.restful-api.dev/objects"
			timeout 15000
			max_send_iterations 5
			redirects true
			ssl true
			history true

			runtime trace_id = guid()
			header "Accept" = "application/json"
			header "X-Correlation-Id" = "{{trace_id}}"

			let sent = request.send()
			stash.Step = "list"
			stash.Status = sent.status
			stash.Count = sent.length()
			stash.Commit()

			request.method = "POST"
			request.url = "https://api.restful-api.dev/objects"
			request.content_type = "application/json"
			request.body = "{\"name\":\"Validation Widget\",\"data\":{\"price\":1849.99}}"
			sent = request.send()
			stash.Step = "create"
			stash.ObjectId = sent.id
			stash.CreatedAt = sent.createdAt
			stash.Commit()

			request.method = "PUT"
			request.url = "https://api.restful-api.dev/objects/7"
			request.body = "{\"name\":\"Validation Widget\",\"data\":{\"price\":2049.99,\"color\":\"silver\"}}"
			sent = request.send()
			stash.Step = "replace"
			stash.UpdatedAt = sent.updatedAt
			stash.Commit()

			request.method = "PATCH"
			request.body = "{\"name\":\"Validation Widget Updated\"}"
			sent = request.send()
			stash.Step = "patch"
			stash.Name = sent.name
			stash.Commit()

			request.method = "DELETE"
			request.body = ""
			sent = request.send()
			stash.Step = "delete"
			stash.DeleteMessage = sent.message
			stash.Commit()

			expect header "Content-Type" contains "json" "json response"
			""",
			new()
			{
				WorkspaceId = Guid.NewGuid(),
				DefaultRequestName = "restful-api QA",
			});
		Assert.IsNotNull(
			compilation.Payload,
			string.Join(Environment.NewLine, compilation.Diagnostics.Select(static diagnostic => $"L{diagnostic.Line}: {diagnostic.Message}")));

		MethodInfo method = typeof(MainPageViewModel).GetMethod(
			"TryValidateCompiledRequestScripts",
			BindingFlags.Instance | BindingFlags.NonPublic)!;
		object?[] arguments = [compilation.Payload, null];

		bool succeeded = (bool)method.Invoke(viewModel, arguments)!;

		Assert.IsTrue(succeeded, arguments[1] as string);
		Assert.AreEqual(string.Empty, arguments[1] as string);
	}

	[TestMethod]
	public void TryValidateCompiledRequestScripts_accepts_restful_api_filtered_queries_with_documented_sample_ids()
	{
		using TestHarness harness = new();
		MainPageViewModel viewModel = harness.CreateViewModel(
			scriptEngine: new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance));
		ForRestScriptCompiler compiler = new(new ForRestScriptParser());

		ForRestScriptCompilationResult compilation = compiler.Compile(
			"""
			name "restful-api QA"
			method GET
			url "https://api.restful-api.dev/objects"
			timeout 15000
			max_send_iterations 4
			redirects true
			ssl true
			history true

			header "Accept" = "application/json"

			let sent = request.send()
			tests.Assert(sent.status >= 200 and sent.status < 300, "list returns 2xx")

			request.url = "https://api.restful-api.dev/objects?id=3&id=5&id=10"
			sent = request.send()
			tests.Assert(sent.status >= 200 and sent.status < 300, "filtered list returns 2xx")
			tests.Equal(3, sent.length(), "filtered list returns requested ids")
			stash.Step = "filtered-list"
			stash.Count = sent.length()
			stash.FirstId = sent[0].id
			stash.Commit()

			expect header "Content-Type" contains "json" "json response"
			""",
			new()
			{
				WorkspaceId = Guid.NewGuid(),
				DefaultRequestName = "restful-api QA",
			});
		Assert.IsNotNull(
			compilation.Payload,
			string.Join(Environment.NewLine, compilation.Diagnostics.Select(static diagnostic => $"L{diagnostic.Line}: {diagnostic.Message}")));

		MethodInfo method = typeof(MainPageViewModel).GetMethod(
			"TryValidateCompiledRequestScripts",
			BindingFlags.Instance | BindingFlags.NonPublic)!;
		object?[] arguments = [compilation.Payload, null];

		bool succeeded = (bool)method.Invoke(viewModel, arguments)!;

		Assert.IsTrue(succeeded, arguments[1] as string);
		Assert.AreEqual(string.Empty, arguments[1] as string);
	}

	[TestMethod]
	public void Deterministic_restful_api_crud_rewrite_remains_valid_after_request_document_normalization()
	{
		using TestHarness harness = new();
		CompilerOnlyExecutionService executionService = new();
		MainPageViewModel viewModel = harness.CreateViewModel(
			executionService: executionService,
			scriptEngine: new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance));
		string source =
			"""
			name "restful-api QA"
			method GET
			url "https://jsonplaceholder.typicode.com/posts/1"
			""";
		string rewrittenSource = BuildDeterministicCrudRewriteSource(source, "restful-api QA");
		string normalized = RequestWorkbenchDocumentNormalizer.NormalizeRequestDocumentSource(rewrittenSource, string.Empty, "restful-api QA");
		ForRestScriptCompilationResult compilation = executionService.Compile(normalized, Guid.NewGuid(), "restful-api QA");

		Assert.IsTrue(
			compilation.Succeeded,
			string.Join(Environment.NewLine, compilation.Diagnostics.Select(static diagnostic => $"L{diagnostic.Line}: {diagnostic.Message}")));
		Assert.IsNotNull(compilation.Payload);
		StringAssert.Contains(normalized, "tests.Assert(sent.status >= 200 and sent.status < 300, \"list returns 2xx\")");
		StringAssert.Contains(normalized, "tests.Assert(sent.status >= 200 and sent.status < 300, \"delete returns 2xx\")");

		MethodInfo method = typeof(MainPageViewModel).GetMethod(
			"TryValidateCompiledRequestScripts",
			BindingFlags.Instance | BindingFlags.NonPublic)!;
		object?[] arguments = [compilation.Payload, null];
		bool succeeded = (bool)method.Invoke(viewModel, arguments)!;

		Assert.IsTrue(succeeded, arguments[1] as string);
		Assert.AreEqual(string.Empty, arguments[1] as string);
	}

	private static string NormalizeLineEndings(string value)
	{
		return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
	}

	private static string BuildDeterministicCrudRewriteSource(string sourceText, string documentTitle)
	{
		MethodInfo method = typeof(AiInlineConversationService).GetMethod(
			"BuildDeterministicRestfulCrudRewriteSource",
			BindingFlags.Static | BindingFlags.NonPublic)!;
		return (string)method.Invoke(null, [sourceText, documentTitle])!;
	}

	private static IAiActiveDocumentHost CreateActiveRequestDocumentHost(MainPageViewModel viewModel, string sourceText)
	{
		Type hostType = typeof(MainPageViewModel).GetNestedType("ActiveRequestDocumentHost", BindingFlags.NonPublic)!;
		return (IAiActiveDocumentHost)Activator.CreateInstance(
			hostType,
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
			binder: null,
			args: [viewModel, sourceText],
			culture: null)!;
	}

	private sealed class TestHarness : IDisposable
	{
		private readonly string _previousConfigFile;
		private readonly string _rootPath;

		public TestHarness()
		{
			_previousConfigFile = Environment.GetEnvironmentVariable("FORREST_CONFIG_FILE") ?? string.Empty;
			_rootPath = Path.Combine(Path.GetTempPath(), "ForRest-MainPageViewModel-Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_rootPath);
			ConfigFilePath = Path.Combine(_rootPath, "settings.toml");
			StateFilePath = Path.Combine(_rootPath, "request-workbench-state.json");
			Environment.SetEnvironmentVariable("FORREST_CONFIG_FILE", ConfigFilePath);
		}

		public string ConfigFilePath { get; }

		public string StateFilePath { get; }

		public MainPageViewModel CreateViewModel(IForRestScriptExecutionService? executionService = null, IAiInlineConversationService? aiInlineConversationService = null, IScriptEngine? scriptEngine = null)
		{
			ThemeConfigStore themeConfigStore = new();
			SettingsTomlTemplate template = new();
			ThemeConfigParser parser = new();
			ThemeConfigNormalizer normalizer = new(template);
			SettingsTomlDocumentService settingsService = new(
				themeConfigStore,
				parser,
				normalizer,
				template,
				new StandardLicenseValidationService(new LicenseValidationOptions("unused-public-key", GracePeriodDays: 30)),
				new TestBuildMetadataProvider(DateTimeOffset.UtcNow));
			WorkbenchAiSettingsProvider aiSettingsProvider = new(themeConfigStore, parser);

			return new MainPageViewModel(
				new FakeThemeService(),
				settingsService,
				new RequestWorkbenchStateStore(StateFilePath),
				executionService ?? new FakeExecutionService(),
				scriptEngine ?? new FakeScriptEngine(),
				new InMemoryExecutionHistoryRepository(),
				new ForRestScriptDocumentTextService(),
				new FakeAppActivationService(),
				aiSettingsProvider,
				aiInlineConversationService ?? new FakeAiInlineConversationService(AiInlineConversationResult.NotHandled(string.Empty)));
		}

		public void Dispose()
		{
			Environment.SetEnvironmentVariable("FORREST_CONFIG_FILE", string.IsNullOrWhiteSpace(_previousConfigFile) ? null : _previousConfigFile);
			if (Directory.Exists(_rootPath))
			{
				Directory.Delete(_rootPath, recursive: true);
			}
		}
	}

	private sealed class FakeThemeService : IThemeService
	{
		private readonly ThemeCatalog _themeCatalog = new();
		private readonly ThemeConfigParser _parser = new();
		private readonly ThemeConfigNormalizer _normalizer = new(new SettingsTomlTemplate());
		private ForRestSettings _currentSettings = new(ShellThemeName.Azure);
		private EventHandler<ThemeChangedEventArgs>? _themeChanged;

		public event EventHandler<ThemeChangedEventArgs>? ThemeChanged
		{
			add => _themeChanged += value;
			remove => _themeChanged -= value;
		}

		public ShellThemeDefinition CurrentTheme => _themeCatalog.GetTheme(ShellThemeName.Azure);

		public ForRestSettings CurrentSettings => _currentSettings;

		public string CurrentStatusMessage => "Ready";

		public string ConfigFilePath => string.Empty;

		public void Start()
		{
		}

		public void PreviewConfigText(string text)
		{
			ThemeNormalizationResult normalized = _normalizer.Normalize(_parser.Parse(text));
			_currentSettings = normalized.Settings;
			ShellThemeDefinition theme = _themeCatalog.GetTheme(normalized.Settings.Theme);
			_themeChanged?.Invoke(this, new ThemeChangedEventArgs(theme, normalized.Settings, $"theme {theme.Name.ToConfigName()}", configNormalized: false, isPreview: true));
		}
	}

	private sealed class FakeAppActivationService : IAppActivationService
	{
		public ActivationSnapshot EvaluateNow() => new("Activated", "Tests", true);
	}

	private sealed class FakeExecutionService : IForRestScriptExecutionService
	{
		private readonly StashTable _stash;
		private readonly bool _throwOnCompile;

		public FakeExecutionService(StashTable? stash = null, bool throwOnCompile = false)
		{
			_stash = stash ?? new();
			_throwOnCompile = throwOnCompile;
		}

		public int CompileCallCount { get; private set; }

		public int ExecuteCallCount { get; private set; }

		public string LastCompiledSource { get; private set; } = string.Empty;

		public string LastExecutedSource { get; private set; } = string.Empty;

		public ForRestScriptCompilationResult Compile(string source, Guid workspaceId, string? defaultRequestName = null)
		{
			CompileCallCount++;
			LastCompiledSource = source;
			if (_throwOnCompile)
			{
				throw new InvalidOperationException("compile boom");
			}

			return new(
				null,
				new ForRestExecutionPayload
				{
					SourceText = source,
					Request = BuildRequestDefinition(workspaceId, defaultRequestName)
				},
				[]);
		}

		public Task<ForRestScriptExecutionOutcome> Execute(
			AppProfile profile,
			WorkspaceSnapshot workspace,
			string source,
			EnvironmentDefinition? environment,
			string? defaultRequestName = null,
			string? preRequestScriptOverride = null,
			CancellationToken cancellationToken = default)
		{
			ExecuteCallCount++;
			LastExecutedSource = source;
			ResponseSnapshot response = new()
			{
				StatusCode = 200,
				ReasonPhrase = "OK",
				ContentType = "application/json",
				SizeBytes = 128,
				DurationMilliseconds = 42,
				Body = "{ \"ok\": true }",
				RawResponse = "HTTP/1.1 200 OK"
			};

			RequestDefinition request = BuildRequestDefinition(workspace.Workspace.Id, defaultRequestName);
			ExecutionRun run = new()
			{
				WorkspaceId = workspace.Workspace.Id,
				RequestId = request.Id,
				RequestName = request.Name,
				TargetUri = request.UrlTemplate,
				RawRequest = "GET https://example.test/mobile",
				Response = response,
				Stash = _stash,
			};

			return Task.FromResult(
				new ForRestScriptExecutionOutcome
				{
					Compilation = new ForRestScriptCompilationResult(
						null,
						new ForRestExecutionPayload
						{
							SourceText = source,
							Request = request
						},
						[]),
					Execution = new RequestExecutionResult
					{
						State = ExecutionState.Completed,
						Runs = [run],
						LatestResponse = response,
						Stash = _stash,
					}
				});
		}

		private static RequestDefinition BuildRequestDefinition(Guid workspaceId, string? defaultRequestName)
		{
			return new RequestDefinition
			{
				WorkspaceId = workspaceId,
				Name = string.IsNullOrWhiteSpace(defaultRequestName) ? "Mobile Test Request" : defaultRequestName,
				Method = HttpMethodKind.Get,
				UrlTemplate = "https://example.test/mobile"
			};
		}
	}

	private sealed class FakeAiInlineConversationService : IAiInlineConversationService
	{
		private readonly AiInlineConversationResult _result;
		private readonly Action<AiInlineConversationRequest>? _onTryHandle;
		private readonly Func<AiInlineConversationRequest, CancellationToken, Task<AiInlineConversationResult>>? _tryHandleAsync;

		public FakeAiInlineConversationService(
			AiInlineConversationResult result,
			Action<AiInlineConversationRequest>? onTryHandle = null,
			Func<AiInlineConversationRequest, CancellationToken, Task<AiInlineConversationResult>>? tryHandleAsync = null)
		{
			_result = result;
			_onTryHandle = onTryHandle;
			_tryHandleAsync = tryHandleAsync;
		}

		public int CallCount { get; private set; }

		public Task<AiInlineConversationResult> TryHandleAsync(AiInlineConversationRequest request, CancellationToken cancellationToken = default)
		{
			CallCount++;
			_onTryHandle?.Invoke(request);
			if (_tryHandleAsync is not null)
			{
				return _tryHandleAsync(request, cancellationToken);
			}

			string updatedText = _result.Handled && string.IsNullOrWhiteSpace(_result.UpdatedText)
				? request.SourceText
				: _result.UpdatedText;
			return Task.FromResult(_result with { UpdatedText = updatedText });
		}
	}

	private sealed class SourceAwareExecutionService : IForRestScriptExecutionService
	{
		public ForRestScriptCompilationResult Compile(string source, Guid workspaceId, string? defaultRequestName = null)
		{
			string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
			string name = ExtractQuotedValue(normalized, "name") ?? defaultRequestName ?? "Untitled Request";
			string methodText = ExtractTokenValue(normalized, "method") ?? "GET";
			string url = ExtractQuotedValue(normalized, "url") ?? "https://example.test/mobile";
			HttpMethodKind method = Enum.TryParse<HttpMethodKind>(methodText, true, out HttpMethodKind parsedMethod)
				? parsedMethod
				: HttpMethodKind.Get;

			return new(
				null,
				new ForRestExecutionPayload
				{
					SourceText = source,
					Request = new RequestDefinition
					{
						WorkspaceId = workspaceId,
						Name = name,
						Method = method,
						UrlTemplate = url,
					},
				},
				[]);
		}

		public Task<ForRestScriptExecutionOutcome> Execute(
			AppProfile profile,
			WorkspaceSnapshot workspace,
			string source,
			EnvironmentDefinition? environment,
			string? defaultRequestName = null,
			string? preRequestScriptOverride = null,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Execution is not used in this metadata test.");
		}

		private static string? ExtractQuotedValue(string source, string keyword)
		{
			string prefix = $"{keyword} \"";
			string? line = source.Split('\n').FirstOrDefault(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
			if (line is null)
			{
				return null;
			}

			int startIndex = line.IndexOf('"');
			int endIndex = line.LastIndexOf('"');
			return startIndex >= 0 && endIndex > startIndex
				? line[(startIndex + 1)..endIndex]
				: null;
		}

		private static string? ExtractTokenValue(string source, string keyword)
		{
			string prefix = $"{keyword} ";
			string? line = source.Split('\n').FirstOrDefault(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
			return line is null ? null : line[prefix.Length..].Trim();
		}
	}

	private sealed class FakeScriptEngine : IScriptEngine
	{
		public ScriptValidationResult Validate(string script)
		{
			return new();
		}

		public Task<ScriptExecutionResult> Run(ScriptExecutionRequest request, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new ScriptExecutionResult
			{
				PreparedRequest = request.PreparedRequest,
				Response = request.Response,
				SentResponse = request.Response,
				RuntimeVariables = [.. request.RuntimeVariables],
			});
		}
	}

	private sealed class FailingScriptEngine(string errorMessage) : IScriptEngine
	{
		public ScriptValidationResult Validate(string script)
		{
			return new()
			{
				ErrorMessage = string.IsNullOrWhiteSpace(script) ? string.Empty : errorMessage,
			};
		}

		public Task<ScriptExecutionResult> Run(ScriptExecutionRequest request, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new ScriptExecutionResult
			{
				PreparedRequest = request.PreparedRequest,
				Response = request.Response,
				SentResponse = request.Response,
				RuntimeVariables = [.. request.RuntimeVariables],
				ErrorMessage = errorMessage,
			});
		}
	}

	private sealed class ValidationAwareExecutionService : IForRestScriptExecutionService
	{
		public int CompileCallCount { get; private set; }

		public ForRestScriptCompilationResult Compile(string source, Guid workspaceId, string? defaultRequestName = null)
		{
			CompileCallCount++;
			return new(
				null,
				new ForRestExecutionPayload
				{
					SourceText = source,
					Request = new RequestDefinition
					{
						WorkspaceId = workspaceId,
						Name = defaultRequestName ?? "Demo",
						Method = HttpMethodKind.Get,
						UrlTemplate = "https://example.test",
						Headers = [],
						Variables = [],
						TestsScript = "expect status == 200 \"returns 200\"",
					},
				},
				[]);
		}

		public Task<ForRestScriptExecutionOutcome> Execute(
			AppProfile profile,
			WorkspaceSnapshot workspace,
			string source,
			EnvironmentDefinition? environment,
			string? defaultRequestName = null,
			string? preRequestScriptOverride = null,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Execution is not used in this AI diagnostics test.");
		}
	}

	private sealed class RuntimeFailureExecutionService : IForRestScriptExecutionService
	{
		public ForRestScriptCompilationResult Compile(string source, Guid workspaceId, string? defaultRequestName = null)
		{
			return new(
				null,
				new ForRestExecutionPayload
				{
					SourceText = source,
					Request = new RequestDefinition
					{
						WorkspaceId = workspaceId,
						Name = defaultRequestName ?? "Demo",
						Method = HttpMethodKind.Get,
						UrlTemplate = "https://api.restful-api.dev/objects",
						Headers = [],
						Variables = [],
						TestsScript = "expect status == 200 \"returns 200\"",
					},
				},
				[]);
		}

		public Task<ForRestScriptExecutionOutcome> Execute(
			AppProfile profile,
			WorkspaceSnapshot workspace,
			string source,
			EnvironmentDefinition? environment,
			string? defaultRequestName = null,
			string? preRequestScriptOverride = null,
			CancellationToken cancellationToken = default)
		{
			ResponseSnapshot response = new()
			{
				StatusCode = 200,
				ReasonPhrase = "OK",
				ContentType = "application/json",
				DurationMilliseconds = 12,
				SizeBytes = 128,
				Body = """[{ "id": "1", "name": "Apple Watch", "data": null }]""",
				RawResponse = "HTTP/1.1 200 OK",
			};

			RequestDefinition request = new()
			{
				WorkspaceId = workspace.Workspace.Id,
				Name = defaultRequestName ?? "Demo",
				Method = HttpMethodKind.Get,
				UrlTemplate = "https://api.restful-api.dev/objects",
			};

			ExecutionRun run = new()
			{
				WorkspaceId = workspace.Workspace.Id,
				RequestId = request.Id,
				RequestName = request.Name,
				State = ExecutionState.Failed,
				TargetUri = request.UrlTemplate,
				RawRequest = "GET https://api.restful-api.dev/objects",
				ErrorMessage = "Cannot perform runtime binding on a null reference.",
				Response = response,
				ConsoleEntries =
				[
					new()
					{
						Level = ConsoleEntryLevel.Error,
						Message = "Cannot perform runtime binding on a null reference.",
					},
				],
			};

			return Task.FromResult(
				new ForRestScriptExecutionOutcome
				{
					Compilation = new ForRestScriptCompilationResult(
						null,
						new ForRestExecutionPayload
						{
							SourceText = source,
							Request = request,
						},
						[]),
					Execution = new RequestExecutionResult
					{
						State = ExecutionState.Failed,
						Runs = [run],
						LatestResponse = response,
						ConsoleEntries =
						[
							new()
							{
								Level = ConsoleEntryLevel.Error,
								Message = "Cannot perform runtime binding on a null reference.",
							},
						],
					},
				});
		}
	}

	private sealed class ArrayRootValidationExecutionService : IForRestScriptExecutionService
	{
		public ForRestScriptCompilationResult Compile(string source, Guid workspaceId, string? defaultRequestName = null)
		{
			return new(
				null,
				new ForRestExecutionPayload
				{
					SourceText = source,
					Request = new RequestDefinition
					{
						WorkspaceId = workspaceId,
						Name = defaultRequestName ?? "Demo",
						Method = HttpMethodKind.Get,
						UrlTemplate = "https://api.restful-api.dev/objects",
						Headers = [],
						Variables = [],
						TestsScript =
							"""
							foreach item in response {
							  log item.name
							}
							""",
					},
				},
				[]);
		}

		public Task<ForRestScriptExecutionOutcome> Execute(
			AppProfile profile,
			WorkspaceSnapshot workspace,
			string source,
			EnvironmentDefinition? environment,
			string? defaultRequestName = null,
			string? preRequestScriptOverride = null,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Execution is not used in this validation test.");
		}
	}

	private sealed class ThrowingTurnExecutor : IAiTurnExecutor
	{
		public Task<AiTurnExecutionResult> ExecuteAsync(AiTurnExecutionRequest request, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("Blank AI prompts should never reach the turn executor.");
		}
	}

	private sealed class ArrayRootValidationScriptEngine : IScriptEngine
	{
		public int RunCallCount { get; private set; }

		public ScriptValidationResult Validate(string script)
		{
			return new();
		}

		public Task<ScriptExecutionResult> Run(ScriptExecutionRequest request, CancellationToken cancellationToken = default)
		{
			RunCallCount++;
			throw new AssertFailedException("Validation should not execute scripts.");
		}
	}

	private sealed class CapturingTurnExecutor(string responseText) : IAiTurnExecutor
	{
		public string? LastPrompt { get; private set; }

		public Task<AiTurnExecutionResult> ExecuteAsync(AiTurnExecutionRequest request, CancellationToken cancellationToken = default)
		{
			LastPrompt = request.Prompt;
			return Task.FromResult(new AiTurnExecutionResult(true, responseText, [], SessionReset: false));
		}
	}

	private sealed class TestBuildMetadataProvider(DateTimeOffset buildDateUtc) : IBuildMetadataProvider
	{
		public DateTimeOffset GetBuildDateUtc() => buildDateUtc;
	}

	private sealed class CompilerOnlyExecutionService : IForRestScriptExecutionService
	{
		private readonly ForRestScriptCompiler _compiler = new(new ForRestScriptParser());

		public ForRestScriptCompilationResult Compile(string source, Guid workspaceId, string? defaultRequestName = null)
		{
			return _compiler.Compile(
				source,
				new()
				{
					WorkspaceId = workspaceId,
					DefaultRequestName = defaultRequestName ?? "Untitled Request",
				});
		}

		public Task<ForRestScriptExecutionOutcome> Execute(
			AppProfile profile,
			WorkspaceSnapshot workspace,
			string source,
			EnvironmentDefinition? environment,
			string? defaultRequestName = null,
			string? preRequestScriptOverride = null,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Execution is not used in this document-update test.");
		}
	}
}
