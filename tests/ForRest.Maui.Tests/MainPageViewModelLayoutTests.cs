using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using ForRest.Maui.ViewModels;
using ForRest.Models;
using ForRest.Services;
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

		public MainPageViewModel CreateViewModel(IForRestScriptExecutionService? executionService = null)
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

			return new MainPageViewModel(
				new FakeThemeService(),
				settingsService,
				new RequestWorkbenchStateStore(StateFilePath),
				executionService ?? new FakeExecutionService(),
				new InMemoryExecutionHistoryRepository(),
				new ForRestScriptDocumentTextService(),
				new FakeAppActivationService());
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

		public event EventHandler<ThemeChangedEventArgs>? ThemeChanged
		{
			add { }
			remove { }
		}

		public ShellThemeDefinition CurrentTheme => _themeCatalog.GetTheme(ShellThemeName.Azure);

		public string CurrentStatusMessage => "Ready";

		public string ConfigFilePath => string.Empty;

		public void Start()
		{
		}
	}

	private sealed class FakeAppActivationService : IAppActivationService
	{
		public ActivationSnapshot EvaluateNow() => new("Activated", "Tests", true);
	}

	private sealed class FakeExecutionService : IForRestScriptExecutionService
	{
		public ForRestScriptCompilationResult Compile(string source, Guid workspaceId, string? defaultRequestName = null)
		{
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
				Response = response
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
						LatestResponse = response
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

	private sealed class TestBuildMetadataProvider(DateTimeOffset buildDateUtc) : IBuildMetadataProvider
	{
		public DateTimeOffset GetBuildDateUtc() => buildDateUtc;
	}
}
