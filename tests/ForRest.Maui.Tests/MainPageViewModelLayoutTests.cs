using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using ForRest.Maui.ViewModels;
using ForRest.Models;
using ForRest.Services;
using ForRest.Services.AI;
using ForRest.Services.Licensing;
using ForRest.Scripting;

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

		public MainPageViewModel CreateViewModel(IForRestScriptExecutionService? executionService = null, IAiInlineConversationService? aiInlineConversationService = null)
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

		public ForRestScriptCompilationResult Compile(string source, Guid workspaceId, string? defaultRequestName = null)
		{
			CompileCallCount++;
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

		public FakeAiInlineConversationService(AiInlineConversationResult result)
		{
			_result = result;
		}

		public int CallCount { get; private set; }

		public Task<AiInlineConversationResult> TryHandleAsync(AiInlineConversationRequest request, CancellationToken cancellationToken = default)
		{
			CallCount++;
			string updatedText = _result.Handled && string.IsNullOrWhiteSpace(_result.UpdatedText)
				? request.SourceText
				: _result.UpdatedText;
			return Task.FromResult(_result with { UpdatedText = updatedText });
		}
	}

	private sealed class TestBuildMetadataProvider(DateTimeOffset buildDateUtc) : IBuildMetadataProvider
	{
		public DateTimeOffset GetBuildDateUtc() => buildDateUtc;
	}
}
