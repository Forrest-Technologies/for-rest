using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using ForRest.Models;
using ForRest.Repositories;
using ForRest.Services;
using ForRest.Scripting;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ForRest.Maui.ViewModels;

public sealed class MainPageViewModel : ObservableObject
{
	private const double DefaultLeftPanePixels = 260d;
	private const double DefaultRightPanePixels = 316d;
	private const double MinLeftPanePixels = 220d;
	private const double MinRightPanePixels = 248d;
	private const double MinCenterPanePixels = 620d;
	private const double SplitterPixels = 14d;
	private const double CompactLayoutBreakpoint = 980d;
	private const double CompactPaneMinWidth = 300d;
	private const double CompactPaneMaxWidth = 440d;
	private const string RequestDocumentKind = "request";
	private const string SettingsDocumentKind = "settings";

	private Color _methodGet = Color.FromArgb("#167C65");
	private Color _methodPost = Color.FromArgb("#176AB8");
	private Color _methodPut = Color.FromArgb("#9A5A1A");
	private Color _methodDelete = Color.FromArgb("#B2433D");
	private Color _methodNeutral = Color.FromArgb("#5D6978");
	private Color _successColor = Color.FromArgb("#1E7A5F");
	private Color _warningColor = Color.FromArgb("#A5691B");
	private Color _dangerColor = Color.FromArgb("#B2433D");
	private readonly SettingsTomlDocumentService _settingsTomlDocumentService;
	private readonly RequestWorkbenchStateStore _requestWorkbenchStateStore;
	private readonly IForRestScriptExecutionService _scriptExecutionService;
	private readonly IExecutionHistoryRepository _executionHistoryRepository;
	private readonly ForRestScriptDocumentTextService _documentTextService;
	private readonly Dictionary<Guid, RequestWorkbenchWorkspaceState> _workspaceStates = [];
	private static readonly Guid HttpBinWorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly Guid JsonPlaceholderWorkspaceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

	private double _leftPanePixels = DefaultLeftPanePixels;
	private double _rightPanePixels = DefaultRightPanePixels;
	private double _leftRestorePixels = DefaultLeftPanePixels;
	private double _rightRestorePixels = DefaultRightPanePixels;
	private double _workbenchWidth = 1360d;
	private bool _leftPaneCollapsed;
	private bool _rightPaneCollapsed;
	private bool _isCompactLayout;
	private bool _isExplorerOverlayOpen;
	private bool _isInspectorOverlayOpen;
	private string _selectedWorkspace;
	private string _selectedEnvironment;
	private string _selectedMethod;
	private string _requestName;
	private string _requestSummary;
	private string _requestLocation;
	private string _requestTarget;
	private string _requestEditorText;
	private string _headersEditorText;
	private string _bodyEditorText;
	private string _scriptEditorText;
	private string _testsEditorText;
	private string _variablesEditorText;
	private string _responseBodyText;
	private string _responseRawText;
	private string _responseState;
	private string _responseTimeStatus;
	private string _responseSizeStatus;
	private string _debugOutputText;
	private string _executionStatus;
	private string _editorThemeKey;
	private ShellThemeName _currentThemeName;
	private string _themeConfigText;
	private string _activeEditorText;
	private string _activeEditorLanguage;
	private string _activeDocumentKind;
	private string _activeDocumentKindLabel;
	private string _activeDocumentLabel;
	private Color _activeDocumentKindColor;
	private string _activeEditorEditableRangesJson;
	private CancellationTokenSource? _settingsSaveSource;
	private CancellationTokenSource? _requestSaveSource;
	private bool _suppressSettingsAutosave;
	private bool _suppressRequestAutosave;
	private bool _suppressDocumentSynchronization;
	private bool _isInitialized;
	private bool _isSending;
	private RequestBodyMode _requestBodyMode = RequestBodyMode.Json;
	private Guid _selectedWorkspaceId;

	public MainPageViewModel(
		IThemeService themeService,
		SettingsTomlDocumentService settingsTomlDocumentService,
		RequestWorkbenchStateStore requestWorkbenchStateStore,
		IForRestScriptExecutionService scriptExecutionService,
		IExecutionHistoryRepository executionHistoryRepository,
		ForRestScriptDocumentTextService documentTextService)
	{
		_settingsTomlDocumentService = settingsTomlDocumentService;
		_requestWorkbenchStateStore = requestWorkbenchStateStore;
		_scriptExecutionService = scriptExecutionService;
		_executionHistoryRepository = executionHistoryRepository;
		_documentTextService = documentTextService;
		ApplyThemePalette(themeService.CurrentTheme, updateCollections: false);
		RequestWorkbenchWorkspaceState starterWorkspace = BuildDefaultWorkspaces().First();
		RequestWorkbenchDocumentState starterDocument = starterWorkspace.Documents.First();
		_selectedWorkspaceId = starterWorkspace.Id;
		_selectedWorkspace = starterWorkspace.Name;
		_selectedEnvironment = starterWorkspace.SelectedEnvironment;
		_selectedMethod = starterDocument.Method;
		_requestName = starterDocument.Title;
		_requestSummary = starterDocument.Summary;
		_requestLocation = starterDocument.Location;
		_requestTarget = BuildDefaultRequestUrl(_requestLocation);
		_requestEditorText = NormalizeLineEndings(starterDocument.RequestSource);
		_headersEditorText = string.Empty;
		_bodyEditorText = string.Empty;
		_scriptEditorText = NormalizeLineEndings(starterDocument.PreRequestScript);
		_testsEditorText = string.Empty;
		_variablesEditorText = string.Empty;
		_responseBodyText = string.Empty;
		_responseRawText = string.Empty;
		_responseState = "Idle";
		_responseTimeStatus = "--";
		_responseSizeStatus = "--";
		_debugOutputText = "Debug output, compile diagnostics, console entries, and exceptions appear here.";
		_executionStatus = themeService.CurrentStatusMessage;
		_currentThemeName = themeService.CurrentTheme.Name;
		_editorThemeKey = themeService.CurrentTheme.MonacoThemeKey;
		_themeConfigText = ReadSettingsText(_currentThemeName);
		_activeEditorText = _requestEditorText;
		_activeEditorLanguage = "forrest";
		_activeDocumentKind = RequestDocumentKind;
		_activeDocumentKindLabel = _selectedMethod;
		_activeDocumentLabel = BuildRequestDocumentLabel(_requestName);
		_activeDocumentKindColor = _methodPost;
		_activeEditorEditableRangesJson = "[]";

		LeftPaneTabs =
		[
			new PaneTabViewModel("explorer", "Explorer", true),
			new PaneTabViewModel("history", "History")
		];

		Workspaces =
		[
			new WorkspaceItemViewModel(starterWorkspace.Id, starterWorkspace.Name, "Starter workspace", true)
		];

		OpenDocuments =
		[];

		CenterTabs =
		[
			new PaneTabViewModel("request", "Request", true),
			new PaneTabViewModel("headers", "Headers"),
			new PaneTabViewModel("body", "Body"),
			new PaneTabViewModel("script", "Script"),
			new PaneTabViewModel("tests", "Tests"),
			new PaneTabViewModel("variables", "Variables")
		];

		RightPaneTabs =
		[
			new PaneTabViewModel("response", "Response", true),
			new PaneTabViewModel("headers", "Headers"),
			new PaneTabViewModel("trace", "Trace"),
			new PaneTabViewModel("raw", "Raw"),
			new PaneTabViewModel("debug", "Debug")
		];

		ExplorerSections = [];

		HistoryItems =
		[];

		ResponseHeaderRows =
		[];

		OutputMetrics =
		[
			new OutputMetricViewModel("Status", "Idle", _methodNeutral),
			new OutputMetricViewModel("Time", "--", _methodNeutral),
			new OutputMetricViewModel("Size", "--", _methodNeutral),
			new OutputMetricViewModel("Type", "n/a", _methodNeutral)
		];

		TraceEntries =
		[
			new TraceEntryViewModel("ready", "request workbench initialized", DateTime.Now.ToString("T"), _methodNeutral)
		];

		EnvironmentOptions =
		[
			"Local",
			"Stage",
			"Prod"
		];

		MethodOptions =
		[
			"GET",
			"POST",
			"PUT",
			"DELETE"
		];

		_workspaceStates[starterWorkspace.Id] = starterWorkspace;
		RebuildWorkspaceCollections(starterWorkspace);
		themeService.ThemeChanged += OnThemeChanged;
		ApplyThemePalette(themeService.CurrentTheme);
		ActivateRequestEditor();
		SyncSupportEditorsFromRequestSource();
		UpdateRequestMetadataFromSource();
	}

	public ObservableCollection<PaneTabViewModel> LeftPaneTabs { get; }

	public ObservableCollection<WorkspaceItemViewModel> Workspaces { get; }

	public ObservableCollection<RequestDocumentViewModel> OpenDocuments { get; }

	public ObservableCollection<PaneTabViewModel> CenterTabs { get; }

	public ObservableCollection<PaneTabViewModel> RightPaneTabs { get; }

	public ObservableCollection<NavigationSectionViewModel> ExplorerSections { get; }

	public ObservableCollection<HistoryEntryViewModel> HistoryItems { get; }

	public ObservableCollection<NameValueRowViewModel> ResponseHeaderRows { get; }

	public ObservableCollection<OutputMetricViewModel> OutputMetrics { get; }

	public ObservableCollection<TraceEntryViewModel> TraceEntries { get; }

	public IReadOnlyList<string> EnvironmentOptions { get; }

	public IReadOnlyList<string> MethodOptions { get; }

	public string SelectedWorkspace
	{
		get => _selectedWorkspace;
		set
		{
			if (SetProperty(ref _selectedWorkspace, value))
			{
				OnPropertyChanged(nameof(WorkspaceBadge));
				if (_isInitialized && !_suppressRequestAutosave)
				{
					ScheduleRequestAutosave();
				}
			}
		}
	}

	public string SelectedEnvironment
	{
		get => _selectedEnvironment;
		set
		{
			if (SetProperty(ref _selectedEnvironment, value))
			{
				OnPropertyChanged(nameof(EnvironmentBadge));
				OnPropertyChanged(nameof(ActiveDocumentSummary));
				if (_isInitialized && !_suppressRequestAutosave)
				{
					ScheduleRequestAutosave();
				}
			}
		}
	}

	public string SelectedMethod
	{
		get => _selectedMethod;
		set
		{
			if (SetProperty(ref _selectedMethod, value))
			{
				RefreshRequestDraftSignature();
				OnPropertyChanged(nameof(SelectedMethodColor));
				OnPropertyChanged(nameof(RequestStateStatus));
				if (IsActiveRequestEditor && string.Equals(GetSelectedCenterTabKey(), "request", StringComparison.Ordinal))
				{
					ActiveDocumentKindLabel = value;
					ActiveDocumentKindColor = SelectedMethodColor;
				}
			}
		}
	}

	public string RequestName
	{
		get => _requestName;
		set
		{
			if (SetProperty(ref _requestName, value))
			{
				OnPropertyChanged(nameof(RequestDocumentLabel));
				OnPropertyChanged(nameof(RequestStateStatus));
				OnPropertyChanged(nameof(ActiveDocumentSummary));
				if (IsActiveRequestEditor)
				{
					ActiveDocumentLabel = RequestDocumentLabel;
				}
			}
		}
	}

	public string RequestSummary
	{
		get => _requestSummary;
		set
		{
			if (SetProperty(ref _requestSummary, value))
			{
				OnPropertyChanged(nameof(ActiveDocumentSummary));
			}
		}
	}

	public string RequestLocation
	{
		get => _requestLocation;
		set
		{
			if (SetProperty(ref _requestLocation, value))
			{
				OnPropertyChanged(nameof(RequestDocumentLabel));
				OnPropertyChanged(nameof(ActiveDocumentSummary));
			}
		}
	}

	public string RequestTarget
	{
		get => _requestTarget;
		set
		{
			if (SetProperty(ref _requestTarget, value))
			{
				RefreshRequestDraftSignature();
			}
		}
	}

	public string RequestEditorText
	{
		get => _requestEditorText;
		set
		{
			if (!SetProperty(ref _requestEditorText, value))
			{
				return;
			}

			if (IsActiveRequestEditor && _activeEditorText != value)
			{
				SetActiveEditorTextInternal(value);
			}
		}
	}

	public string HeadersEditorText
	{
		get => _headersEditorText;
		set => SetProperty(ref _headersEditorText, value);
	}

	public string BodyEditorText
	{
		get => _bodyEditorText;
		set => SetProperty(ref _bodyEditorText, value);
	}

	public string ScriptEditorText
	{
		get => _scriptEditorText;
		set => SetProperty(ref _scriptEditorText, value);
	}

	public string TestsEditorText
	{
		get => _testsEditorText;
		set => SetProperty(ref _testsEditorText, value);
	}

	public string VariablesEditorText
	{
		get => _variablesEditorText;
		set => SetProperty(ref _variablesEditorText, value);
	}

	public string ResponseBodyText
	{
		get => _responseBodyText;
		set => SetProperty(ref _responseBodyText, value);
	}

	public string ResponseRawText
	{
		get => _responseRawText;
		set => SetProperty(ref _responseRawText, value);
	}

	public string DebugOutputText
	{
		get => _debugOutputText;
		set => SetProperty(ref _debugOutputText, value);
	}

	public string EditorThemeKey
	{
		get => _editorThemeKey;
		set => SetProperty(ref _editorThemeKey, value);
	}

	public string ActiveEditorText
	{
		get => _activeEditorText;
		set
		{
			if (IsActiveSettingsEditor &&
			    string.IsNullOrWhiteSpace(value) &&
			    !string.IsNullOrWhiteSpace(_themeConfigText))
			{
				OnPropertyChanged(nameof(ActiveEditorText));
				return;
			}

			if (!SetProperty(ref _activeEditorText, value))
			{
				return;
			}

			if (IsActiveSettingsEditor)
			{
				_themeConfigText = value;
				if (!_suppressSettingsAutosave)
				{
					ScheduleSettingsAutosave();
				}
			}
			else
			{
				ApplyRequestEditorChange(value);
			}
		}
	}

	public string ActiveEditorLanguage
	{
		get => _activeEditorLanguage;
		set => SetProperty(ref _activeEditorLanguage, value);
	}

	public string ActiveEditorEditableRangesJson
	{
		get => _activeEditorEditableRangesJson;
		set => SetProperty(ref _activeEditorEditableRangesJson, value);
	}

	public string ActiveDocumentKindLabel
	{
		get => _activeDocumentKindLabel;
		set => SetProperty(ref _activeDocumentKindLabel, value);
	}

	public string ActiveDocumentLabel
	{
		get => _activeDocumentLabel;
		set => SetProperty(ref _activeDocumentLabel, value);
	}

	public Color ActiveDocumentKindColor
	{
		get => _activeDocumentKindColor;
		set => SetProperty(ref _activeDocumentKindColor, value);
	}

	public string ResponseState
	{
		get => _responseState;
		set
		{
			if (SetProperty(ref _responseState, value))
			{
				OnPropertyChanged(nameof(OpenTabsStatus));
			}
		}
	}

	public bool IsCompactLayout => _isCompactLayout;

	public bool IsDesktopLayout => !_isCompactLayout;

	public bool IsLeftPaneVisible => !_isCompactLayout && !_leftPaneCollapsed;

	public bool IsRightPaneVisible => !_isCompactLayout && !_rightPaneCollapsed;

	public bool IsExplorerOverlayVisible => _isCompactLayout && _isExplorerOverlayOpen;

	public bool IsInspectorOverlayVisible => _isCompactLayout && _isInspectorOverlayOpen;

	public bool IsOverlayBackdropVisible => IsExplorerOverlayVisible || IsInspectorOverlayVisible;

	public double CompactPaneWidth
	{
		get
		{
			double available = Math.Max(CompactPaneMinWidth, _workbenchWidth - 24d);
			return Math.Min(CompactPaneMaxWidth, available);
		}
	}

	public GridLength LeftPaneWidth => new(IsLeftPaneVisible ? _leftPanePixels : 0d, GridUnitType.Absolute);

	public GridLength RightPaneWidth => new(IsRightPaneVisible ? _rightPanePixels : 0d, GridUnitType.Absolute);

	public GridLength LeftSplitterWidth => new(IsLeftPaneVisible ? SplitterPixels : 0d, GridUnitType.Absolute);

	public GridLength RightSplitterWidth => new(IsRightPaneVisible ? SplitterPixels : 0d, GridUnitType.Absolute);

	public string WorkspaceBadge => SelectedWorkspace;

	public string EnvironmentBadge => $"Env {SelectedEnvironment}";

	public string ShellDescriptor => "Fluid three-pane engineering workbench";

	public string ActiveDocumentSummary => $"{SelectedEnvironment}  {RequestLocation}  {RequestSummary}";

	public string RequestStateStatus => $"{SelectedMethod}  {RequestName}";

	public string RequestDocumentLabel => BuildRequestDocumentLabel(RequestName);

	public string OpenTabsStatus => $"{OpenDocuments.Count} docs  {ResponseState}";

	public string TimingStatus => _isCompactLayout ? "Compact overlay shell" : "Three-pane desktop shell";

	public string ExecutionStatus
	{
		get => _executionStatus;
		set => SetProperty(ref _executionStatus, value);
	}

	public string ResponseSizeStatus => _responseSizeStatus;

	public string ResponseTimeStatus => _responseTimeStatus;

	public bool ShowLeftPaneRestoreButton => !_isCompactLayout && _leftPaneCollapsed;

	public bool ShowRightPaneRestoreButton => !_isCompactLayout && _rightPaneCollapsed;

	public bool CanSend => !_isSending && IsActiveRequestEditor;

	public Color SelectedMethodColor => SelectedMethod switch
	{
		"GET" => _methodGet,
		"POST" => _methodPost,
		"PUT" => _methodPut,
		"DELETE" => _methodDelete,
		_ => _methodNeutral
	};

	public string CenterSurfaceStatus => CenterTabs.FirstOrDefault(tab => tab.IsSelected)?.Key switch
	{
		"request" => "HTTP-shaped draft surface",
		"headers" => "Text-defined request header surface",
		"body" => "Primary payload editor surface",
		"script" => "Pre-execution logic surface",
		"tests" => "Assertion and verification surface",
		"variables" => "Workspace and request variables surface",
		_ => "Editor-first center surface"
	};

	public string RightSurfaceStatus => RightPaneTabs.FirstOrDefault(tab => tab.IsSelected)?.Key switch
	{
		"response" => "Primary response viewer",
		"headers" => "Response metadata and transport details",
		"trace" => "Execution trace and feedback",
		"raw" => "Raw transport output",
		"debug" => "Debug details, diagnostics, and exceptions",
		_ => "Inspection surface"
	};

	public bool IsExplorerTabVisible => IsTabSelected(LeftPaneTabs, "explorer");

	public bool IsHistoryTabVisible => IsTabSelected(LeftPaneTabs, "history");

	public bool IsRequestTabVisible => IsTabSelected(CenterTabs, "request");

	public bool IsHeadersTabVisible => IsTabSelected(CenterTabs, "headers");

	public bool IsBodyTabVisible => IsTabSelected(CenterTabs, "body");

	public bool IsScriptTabVisible => IsTabSelected(CenterTabs, "script");

	public bool IsTestsTabVisible => IsTabSelected(CenterTabs, "tests");

	public bool IsVariablesTabVisible => IsTabSelected(CenterTabs, "variables");

	public bool IsInspectorResponseVisible => IsTabSelected(RightPaneTabs, "response");

	public bool IsInspectorHeadersVisible => IsTabSelected(RightPaneTabs, "headers");

	public bool IsInspectorTraceVisible => IsTabSelected(RightPaneTabs, "trace");

	public bool IsInspectorRawVisible => IsTabSelected(RightPaneTabs, "raw");

	public bool IsInspectorDebugVisible => IsTabSelected(RightPaneTabs, "debug");

	private bool IsActiveRequestEditor => string.Equals(_activeDocumentKind, RequestDocumentKind, StringComparison.Ordinal);

	private bool IsActiveSettingsEditor => string.Equals(_activeDocumentKind, SettingsDocumentKind, StringComparison.Ordinal);

	public async Task InitializeAsync()
	{
		if (_isInitialized)
		{
			return;
		}

		List<RequestWorkbenchWorkspaceState> defaults = BuildDefaultWorkspaces();
		RequestWorkbenchState state = await _requestWorkbenchStateStore.LoadAsync(defaults);

		_workspaceStates.Clear();
		Workspaces.Clear();
		foreach (RequestWorkbenchWorkspaceState workspace in state.Workspaces)
		{
			_workspaceStates[workspace.Id] = workspace;
			Workspaces.Add(
				new WorkspaceItemViewModel(
					workspace.Id,
					workspace.Name,
					workspace.Documents.Count == 1 ? "1 request" : $"{workspace.Documents.Count} requests",
					false));
		}

		Guid selectedWorkspaceId = Workspaces.Any(item => item.Id == state.SelectedWorkspaceId)
			? state.SelectedWorkspaceId
			: Workspaces.FirstOrDefault()?.Id ?? Guid.Empty;

		ApplyWorkspaceSelection(selectedWorkspaceId);
		_isInitialized = true;
	}

	public async Task SendAsync()
	{
		if (!CanSend)
		{
			return;
		}

		_isSending = true;
		OnPropertyChanged(nameof(CanSend));
		try
		{
			await PersistCurrentRequestAsync();
			ForRestScriptExecutionOutcome outcome = await _scriptExecutionService.Execute(
				BuildProfile(),
				BuildWorkspaceSnapshot(),
				RequestEditorText,
				BuildEnvironmentDefinition(),
				RequestName,
				ScriptEditorText);

			if (!outcome.Compilation.Succeeded || outcome.Compilation.Payload is null)
			{
				ApplyCompilationFailure(outcome.Compilation.Diagnostics);
				return;
			}

			RequestName = outcome.Compilation.Payload.Request.Name;
			SelectedMethod = outcome.Compilation.Payload.Request.Method.ToString().ToUpperInvariant();
			RequestTarget = outcome.Compilation.Payload.Request.UrlTemplate;
			RequestSummary = string.IsNullOrWhiteSpace(RequestSummary) ? $"{SelectedMethod} request" : RequestSummary;
			UpdateCurrentDocumentMetadata();
			ResponseState = outcome.Execution?.LatestResponse is { } response
				? $"{response.StatusCode} {response.ReasonPhrase}".Trim()
				: outcome.Execution?.State.ToString() ?? "Compiled";
			ResponseBodyText = outcome.Execution?.LatestResponse?.Body ?? string.Empty;
			ResponseRawText = outcome.Execution?.LatestResponse?.RawResponse ?? string.Empty;
			_responseTimeStatus = outcome.Execution?.LatestResponse is { } latestResponse
				? $"{latestResponse.DurationMilliseconds} ms"
				: "--";
			_responseSizeStatus = outcome.Execution?.LatestResponse is { } latestSizeResponse
				? FormatResponseSize(latestSizeResponse.SizeBytes)
				: "--";
			ExecutionStatus = outcome.Execution?.State == ExecutionState.Completed
				? "Sent via .frs execution pipeline"
				: outcome.Execution?.State == ExecutionState.Failed
					? "Execution failed"
					: "Compiled request document";
			DebugOutputText = BuildDebugOutput(outcome);

			ResponseHeaderRows.Clear();
			foreach (KeyValueDefinition header in outcome.Execution?.LatestResponse?.Headers ?? [])
			{
				ResponseHeaderRows.Add(new NameValueRowViewModel(header.Key, header.Value, "response"));
			}

			HistoryItems.Clear();
			List<ExecutionRun> historyRuns = await _executionHistoryRepository.Load(GetSelectedWorkspaceState()?.Id ?? HttpBinWorkspaceId);
			foreach (ExecutionRun run in historyRuns.Take(8))
			{
				HistoryItems.Add(
					new HistoryEntryViewModel(
						SelectedMethod,
						run.RequestName,
						run.Response is null ? run.State.ToString() : $"{run.Response.StatusCode} in {run.Response.DurationMilliseconds} ms",
						run.StartedUtc.ToLocalTime().ToString("t"),
						ResolveMethodAccent(SelectedMethod)));
			}

			TraceEntries.Clear();
			TraceEntries.Add(new TraceEntryViewModel("compile", "request document compiled", DateTime.Now.ToString("T"), _methodNeutral));
			if (outcome.Execution?.LatestResponse is not null)
			{
				TraceEntries.Add(new TraceEntryViewModel("send", ResponseState, DateTime.Now.ToString("T"), SelectedMethodColor));
			}

			if (outcome.Execution?.Tests.Count > 0)
			{
				string summary = $"{outcome.Execution.Tests.Count(static item => item.State == TestOutcomeState.Passed)}/{outcome.Execution.Tests.Count} tests passed";
				TraceEntries.Add(new TraceEntryViewModel("tests", summary, DateTime.Now.ToString("T"), outcome.Execution.Tests.All(static item => item.State == TestOutcomeState.Passed) ? _successColor : _dangerColor));
			}

			OutputMetrics.Clear();
			OutputMetrics.Add(new OutputMetricViewModel("Status", ResponseState, ResponseState.StartsWith("2", StringComparison.Ordinal) ? _successColor : _dangerColor));
			OutputMetrics.Add(new OutputMetricViewModel("Time", _responseTimeStatus, SelectedMethodColor));
			OutputMetrics.Add(new OutputMetricViewModel("Size", _responseSizeStatus, _methodNeutral));
			OutputMetrics.Add(new OutputMetricViewModel("Type", outcome.Execution?.LatestResponse?.ContentType ?? "n/a", SelectedMethodColor));
			OnPropertyChanged(nameof(ResponseTimeStatus));
			OnPropertyChanged(nameof(ResponseSizeStatus));

			if (outcome.Execution?.State == ExecutionState.Failed)
			{
				FocusRightPaneTab("debug");
			}
		}
		catch (Exception exception)
		{
			ResponseState = "Failed";
			ExecutionStatus = exception.Message;
			_responseTimeStatus = "--";
			_responseSizeStatus = "--";
			ResponseBodyText = string.Empty;
			ResponseRawText = string.Empty;
			DebugOutputText = exception.ToString();
			ResponseHeaderRows.Clear();
			OutputMetrics.Clear();
			OutputMetrics.Add(new OutputMetricViewModel("Status", "Failed", _dangerColor));
			OutputMetrics.Add(new OutputMetricViewModel("Time", "--", _methodNeutral));
			OutputMetrics.Add(new OutputMetricViewModel("Size", "--", _methodNeutral));
			OutputMetrics.Add(new OutputMetricViewModel("Type", "n/a", _methodNeutral));
			TraceEntries.Clear();
			TraceEntries.Add(new TraceEntryViewModel("error", exception.Message, DateTime.Now.ToString("T"), _dangerColor));
			OnPropertyChanged(nameof(ResponseTimeStatus));
			OnPropertyChanged(nameof(ResponseSizeStatus));
			FocusRightPaneTab("debug");
		}
		finally
		{
			_isSending = false;
			OnPropertyChanged(nameof(CanSend));
		}
	}

	public void ToggleLeftPane()
	{
		if (_isCompactLayout)
		{
			bool nextState = !_isExplorerOverlayOpen;
			_isExplorerOverlayOpen = nextState;
			_isInspectorOverlayOpen = false;
			NotifyOverlayChanged();
			return;
		}

		if (_leftPaneCollapsed)
		{
			_leftPaneCollapsed = false;
			_leftPanePixels = _leftRestorePixels;
		}
		else
		{
			_leftRestorePixels = _leftPanePixels;
			_leftPaneCollapsed = true;
		}

		NotifyPaneLayoutChanged();
	}

	public void ToggleRightPane()
	{
		if (_isCompactLayout)
		{
			bool nextState = !_isInspectorOverlayOpen;
			_isInspectorOverlayOpen = nextState;
			_isExplorerOverlayOpen = false;
			NotifyOverlayChanged();
			return;
		}

		if (_rightPaneCollapsed)
		{
			_rightPaneCollapsed = false;
			_rightPanePixels = _rightRestorePixels;
		}
		else
		{
			_rightRestorePixels = _rightPanePixels;
			_rightPaneCollapsed = true;
		}

		NotifyPaneLayoutChanged();
	}

	public void DismissOverlays()
	{
		if (!_isCompactLayout || (!_isExplorerOverlayOpen && !_isInspectorOverlayOpen))
		{
			return;
		}

		_isExplorerOverlayOpen = false;
		_isInspectorOverlayOpen = false;
		NotifyOverlayChanged();
	}

	public void ResizeLeftPane(double requestedWidth, double totalWidth)
	{
		if (_isCompactLayout || _leftPaneCollapsed)
		{
			return;
		}

		_leftPanePixels = ClampLeftPane(requestedWidth, totalWidth);
		NotifyPaneLayoutChanged();
	}

	public void ResizeRightPane(double requestedWidth, double totalWidth)
	{
		if (_isCompactLayout || _rightPaneCollapsed)
		{
			return;
		}

		_rightPanePixels = ClampRightPane(requestedWidth, totalWidth);
		NotifyPaneLayoutChanged();
	}

	public void UpdateLayoutMode(double availableWidth)
	{
		if (availableWidth <= 0)
		{
			return;
		}

		_workbenchWidth = availableWidth;
		bool isCompact = availableWidth < CompactLayoutBreakpoint;
		bool layoutChanged = _isCompactLayout != isCompact;
		_isCompactLayout = isCompact;

		if (!_isCompactLayout || layoutChanged)
		{
			_isExplorerOverlayOpen = false;
			_isInspectorOverlayOpen = false;
		}

		OnPropertyChanged(nameof(IsCompactLayout));
		OnPropertyChanged(nameof(IsDesktopLayout));
		OnPropertyChanged(nameof(CompactPaneWidth));
		OnPropertyChanged(nameof(TimingStatus));

		if (!_isCompactLayout)
		{
			ConstrainPaneLayout(availableWidth);
		}
		else
		{
			NotifyPaneLayoutChanged();
			NotifyOverlayChanged();
		}
	}

	public void ConstrainPaneLayout(double totalWidth)
	{
		if (_isCompactLayout || totalWidth <= 0)
		{
			return;
		}

		if (!_leftPaneCollapsed)
		{
			_leftPanePixels = ClampLeftPane(_leftPanePixels, totalWidth);
		}

		if (!_rightPaneCollapsed)
		{
			_rightPanePixels = ClampRightPane(_rightPanePixels, totalWidth);
		}

		NotifyPaneLayoutChanged();
	}

	public void SelectLeftPaneTab(PaneTabViewModel? tab)
	{
		if (tab is null)
		{
			return;
		}

		SetSelected(LeftPaneTabs, tab);
		OnPropertyChanged(nameof(IsExplorerTabVisible));
		OnPropertyChanged(nameof(IsHistoryTabVisible));
	}

	public void SelectCenterTab(PaneTabViewModel? tab)
	{
		if (tab is null)
		{
			return;
		}

		SetSelected(CenterTabs, tab);
		OnPropertyChanged(nameof(IsRequestTabVisible));
		OnPropertyChanged(nameof(IsHeadersTabVisible));
		OnPropertyChanged(nameof(IsBodyTabVisible));
		OnPropertyChanged(nameof(IsScriptTabVisible));
		OnPropertyChanged(nameof(IsTestsTabVisible));
		OnPropertyChanged(nameof(IsVariablesTabVisible));
		OnPropertyChanged(nameof(CenterSurfaceStatus));
		ActivateCurrentCenterTabEditor();
	}

	public void SelectRightPaneTab(PaneTabViewModel? tab)
	{
		if (tab is null)
		{
			return;
		}

		SetSelected(RightPaneTabs, tab);
		OnPropertyChanged(nameof(IsInspectorResponseVisible));
		OnPropertyChanged(nameof(IsInspectorHeadersVisible));
		OnPropertyChanged(nameof(IsInspectorTraceVisible));
		OnPropertyChanged(nameof(IsInspectorRawVisible));
		OnPropertyChanged(nameof(IsInspectorDebugVisible));
		OnPropertyChanged(nameof(RightSurfaceStatus));
	}

	public void SelectWorkspace(WorkspaceItemViewModel? workspace)
	{
		if (workspace is null || workspace.Id == _selectedWorkspaceId)
		{
			return;
		}

		PersistActiveRequestInBackground();
		ApplyWorkspaceSelection(workspace.Id);
		if (_isInitialized)
		{
			_ = PersistWorkbenchStateInBackground();
		}
	}

	public void AddWorkspace()
	{
		string workspaceName = BuildNextWorkspaceName();
		RequestWorkbenchWorkspaceState workspace = BuildUserWorkspace(workspaceName);
		_workspaceStates[workspace.Id] = workspace;
		Workspaces.Add(
			new WorkspaceItemViewModel(
				workspace.Id,
				workspace.Name,
				workspace.Documents.Count == 1 ? "1 request" : $"{workspace.Documents.Count} requests",
				false));

		ApplyWorkspaceSelection(workspace.Id);
		if (_isInitialized)
		{
			_ = PersistWorkbenchStateInBackground();
		}
	}

	public void SelectDocument(RequestDocumentViewModel? document)
	{
		if (document is null)
		{
			return;
		}

		PersistActiveRequestInBackground();

		foreach (RequestDocumentViewModel item in OpenDocuments)
		{
			item.IsSelected = ReferenceEquals(item, document);
		}

		ApplyRequestSelection(document.Title, document.Method, document.Summary, document.Location);
		SelectExplorerItemByContext(document.Location);
		ActivateRequestEditor();
	}

	public void SelectExplorerItem(NavigationItemViewModel? item)
	{
		if (item is null)
		{
			return;
		}

		PersistActiveRequestInBackground();

		foreach (NavigationItemViewModel entry in ExplorerSections.SelectMany(section => section.Items))
		{
			entry.IsSelected = ReferenceEquals(entry, item);
		}

		if (string.Equals(item.DocumentKind, SettingsDocumentKind, StringComparison.Ordinal))
		{
			ActivateSettingsEditor(item);
			return;
		}

		string method = item.Method ?? SelectedMethod;
		ApplyRequestSelection(item.Title, method, item.Detail, item.Context);
		SelectDocumentByLocation(item.Context);
	}

	private void ApplyWorkspaceSelection(Guid workspaceId)
	{
		if (!_workspaceStates.TryGetValue(workspaceId, out RequestWorkbenchWorkspaceState? workspace))
		{
			return;
		}

		_selectedWorkspaceId = workspace.Id;
		_suppressRequestAutosave = true;
		try
		{
			SelectedWorkspace = workspace.Name;
			SelectedEnvironment = workspace.SelectedEnvironment;
			RebuildWorkspaceCollections(workspace);

			RequestWorkbenchDocumentState? selectedDocument = workspace.Documents.FirstOrDefault(
				document => string.Equals(document.Location, workspace.SelectedDocumentLocation, StringComparison.OrdinalIgnoreCase))
				?? workspace.Documents.FirstOrDefault();

			if (selectedDocument is null)
			{
				return;
			}

			foreach (RequestDocumentViewModel document in OpenDocuments)
			{
				document.IsSelected = string.Equals(document.Location, selectedDocument.Location, StringComparison.OrdinalIgnoreCase);
			}

			SelectExplorerItemByContext(selectedDocument.Location);
			ApplyRequestSelection(selectedDocument.Title, selectedDocument.Method, selectedDocument.Summary, selectedDocument.Location);
		}
		finally
		{
			_suppressRequestAutosave = false;
		}

		OnPropertyChanged(nameof(OpenTabsStatus));
	}

	private void RebuildWorkspaceCollections(RequestWorkbenchWorkspaceState workspace)
	{
		foreach (WorkspaceItemViewModel item in Workspaces)
		{
			item.IsSelected = item.Id == workspace.Id;
			if (_workspaceStates.TryGetValue(item.Id, out RequestWorkbenchWorkspaceState? state))
			{
				item.Title = state.Name;
				item.Subtitle = state.Documents.Count == 1 ? "1 request" : $"{state.Documents.Count} requests";
			}
		}

		OpenDocuments.Clear();
		foreach (RequestWorkbenchDocumentState document in workspace.Documents)
		{
			OpenDocuments.Add(new RequestDocumentViewModel(document.Title, document.Method, document.Summary, document.Location, false, false));
		}

		ExplorerSections.Clear();
		ExplorerSections.Add(
			new NavigationSectionViewModel(
				"Workspace",
				[
					new NavigationItemViewModel(
						"CFG",
						"settings.toml",
						$"Theme and shell settings for {workspace.Name}",
						$"~/{BuildRequestDocumentLabel(workspace.Name).Replace(".frs", string.Empty, StringComparison.Ordinal)}/settings",
						_methodNeutral,
						depth: 0,
						documentKind: SettingsDocumentKind,
						editorLanguage: "settings-toml")
				]));
		ExplorerSections.Add(
			new NavigationSectionViewModel(
				"Requests",
				[
					.. workspace.Documents.Select(
						document => new NavigationItemViewModel(
							document.Method.ToUpperInvariant(),
							document.Title,
							document.Summary,
							document.Location,
							ResolveMethodAccent(document.Method),
							document.Method))
				]));
	}

	private RequestWorkbenchWorkspaceState? GetSelectedWorkspaceState()
	{
		return _workspaceStates.TryGetValue(_selectedWorkspaceId, out RequestWorkbenchWorkspaceState? workspace)
			? workspace
			: null;
	}

	private void UpdateSelectedWorkspaceState(Func<RequestWorkbenchWorkspaceState, RequestWorkbenchWorkspaceState> updater)
	{
		RequestWorkbenchWorkspaceState? currentWorkspace = GetSelectedWorkspaceState();
		if (currentWorkspace is null)
		{
			return;
		}

		RequestWorkbenchWorkspaceState updatedWorkspace = updater(currentWorkspace);
		_workspaceStates[updatedWorkspace.Id] = updatedWorkspace;

		WorkspaceItemViewModel? workspaceItem = Workspaces.FirstOrDefault(item => item.Id == updatedWorkspace.Id);
		if (workspaceItem is not null)
		{
			workspaceItem.Title = updatedWorkspace.Name;
			workspaceItem.Subtitle = updatedWorkspace.Documents.Count == 1 ? "1 request" : $"{updatedWorkspace.Documents.Count} requests";
		}
	}

	private RequestWorkbenchState BuildWorkbenchState()
	{
		List<RequestWorkbenchWorkspaceState> workspaces = Workspaces
			.Select(item => _workspaceStates.TryGetValue(item.Id, out RequestWorkbenchWorkspaceState? workspace) ? workspace : null)
			.Where(static item => item is not null)
			.Cast<RequestWorkbenchWorkspaceState>()
			.ToList();

		return new()
		{
			SelectedWorkspaceId = _selectedWorkspaceId,
			Workspaces = workspaces
		};
	}

	private async Task PersistWorkbenchStateInBackground()
	{
		try
		{
			await _requestWorkbenchStateStore.SaveAsync(BuildWorkbenchState());
		}
		catch
		{
		}
	}

	private static List<RequestWorkbenchDocumentState> UpsertDocument(
		IReadOnlyList<RequestWorkbenchDocumentState> documents,
		RequestWorkbenchDocumentState current)
	{
		List<RequestWorkbenchDocumentState> updated = [];
		bool replaced = false;
		foreach (RequestWorkbenchDocumentState document in documents)
		{
			if (string.Equals(document.Location, current.Location, StringComparison.OrdinalIgnoreCase))
			{
				updated.Add(current);
				replaced = true;
				continue;
			}

			updated.Add(document);
		}

		if (!replaced)
		{
			updated.Add(current);
		}

		return updated;
	}

	private string BuildNextWorkspaceName()
	{
		int suffix = Workspaces.Count + 1;
		string candidate = $"Workspace {suffix}";
		HashSet<string> existingNames = Workspaces
			.Select(item => item.Title)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		while (existingNames.Contains(candidate))
		{
			suffix++;
			candidate = $"Workspace {suffix}";
		}

		return candidate;
	}

	private static RequestWorkbenchWorkspaceState BuildUserWorkspace(string workspaceName)
	{
		return BuildWorkspace(
			Guid.NewGuid(),
			workspaceName,
			[
				BuildDefaultDocumentState("Get Headers", "GET", "Inspect request headers round-trip", "/requests/httpbin/headers"),
				BuildDefaultDocumentState("Post Echo", "POST", "Echo a JSON payload through httpbin", "/requests/httpbin/post"),
				BuildDefaultDocumentState("Get UUID", "GET", "Fetch a quick generated identifier", "/requests/httpbin/uuid")
			]);
	}

	private static bool IsTabSelected(IEnumerable<PaneTabViewModel> tabs, string key)
	{
		return tabs.Any(tab => tab.Key == key && tab.IsSelected);
	}

	private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
	{
		_currentThemeName = e.Theme.Name;
		EditorThemeKey = e.Theme.MonacoThemeKey;
		ExecutionStatus = e.StatusMessage;
		ApplyThemePalette(e.Theme);
		UpdateSettingsTextFromDisk(_currentThemeName);
	}

	private void ApplyThemePalette(ShellThemeDefinition theme, bool updateCollections = true)
	{
		_methodGet = ThemeSupport.ToColor(theme.Colors.MethodGetColor);
		_methodPost = ThemeSupport.ToColor(theme.Colors.MethodPostColor);
		_methodPut = ThemeSupport.ToColor(theme.Colors.MethodPutColor);
		_methodDelete = ThemeSupport.ToColor(theme.Colors.MethodDeleteColor);
		_methodNeutral = ThemeSupport.ToColor(theme.Colors.MethodNeutralColor);
		_successColor = ThemeSupport.ToColor(theme.Colors.SuccessColor);
		_warningColor = ThemeSupport.ToColor(theme.Colors.WarningColor);
		_dangerColor = ThemeSupport.ToColor(theme.Colors.DangerColor);

		if (!updateCollections)
		{
			return;
		}

		foreach (NavigationItemViewModel item in ExplorerSections.SelectMany(section => section.Items))
		{
			item.AccentColor = ResolveNavigationAccent(item);
		}

		foreach (HistoryEntryViewModel item in HistoryItems)
		{
			item.AccentColor = ResolveMethodAccent(item.Method);
		}

		foreach (TraceEntryViewModel item in TraceEntries)
		{
			item.AccentColor = item.Title switch
			{
				"send" => _methodPost,
				"inspect" => _methodGet,
				_ => _methodNeutral
			};
		}

		foreach (OutputMetricViewModel item in OutputMetrics)
		{
			item.AccentColor = item.Label switch
			{
				"Status" => _successColor,
				"Time" => _methodPost,
				"Type" => _methodPost,
				_ => _methodNeutral
			};
		}

		OnPropertyChanged(nameof(SelectedMethodColor));
		if (IsActiveRequestEditor)
		{
			ActivateCurrentCenterTabEditor();
		}
		else if (IsActiveSettingsEditor)
		{
			ActiveDocumentKindColor = _methodNeutral;
		}
	}

	private Color ResolveNavigationAccent(NavigationItemViewModel item)
	{
		if (string.Equals(item.DocumentKind, SettingsDocumentKind, StringComparison.Ordinal) ||
		    string.IsNullOrWhiteSpace(item.Method))
		{
			return _methodNeutral;
		}

		return ResolveMethodAccent(item.Method);
	}

	private Color ResolveMethodAccent(string? method)
	{
		return method?.ToUpperInvariant() switch
		{
			"GET" => _methodGet,
			"POST" => _methodPost,
			"PUT" => _methodPut,
			"DELETE" => _methodDelete,
			_ => _methodNeutral
		};
	}

	private void RefreshRequestDraftSignature()
	{
	}

	private void SetSelected(IEnumerable<PaneTabViewModel> tabs, PaneTabViewModel selected)
	{
		foreach (PaneTabViewModel tab in tabs)
		{
			tab.IsSelected = ReferenceEquals(tab, selected);
		}
	}

	private void ApplyRequestSelection(string title, string method, string summary, string location)
	{
		RequestWorkbenchWorkspaceState workspace = GetSelectedWorkspaceState() ?? BuildUserWorkspace("Workspace");
		RequestWorkbenchDocumentState state = workspace.Documents.FirstOrDefault(
			document => string.Equals(document.Location, location, StringComparison.OrdinalIgnoreCase))
			?? BuildDefaultDocumentState(title, method, summary, location);

		_suppressRequestAutosave = true;
		_suppressDocumentSynchronization = true;
		try
		{
			RequestName = state.Title;
			SelectedMethod = state.Method;
			RequestSummary = state.Summary;
			RequestLocation = state.Location;
			RequestEditorText = NormalizeLineEndings(state.RequestSource);
			ScriptEditorText = NormalizeLineEndings(state.PreRequestScript);
			SyncSupportEditorsFromRequestSource();
		}
		finally
		{
			_suppressDocumentSynchronization = false;
			_suppressRequestAutosave = false;
		}

		UpdateCurrentDocumentMetadata();
		ActivateRequestEditor();
	}

	private void SelectDocumentByLocation(string location)
	{
		RequestDocumentViewModel? matchingDocument = OpenDocuments.FirstOrDefault(
			document => string.Equals(document.Location, location, StringComparison.OrdinalIgnoreCase));
		if (matchingDocument is null)
		{
			return;
		}

		foreach (RequestDocumentViewModel document in OpenDocuments)
		{
			document.IsSelected = ReferenceEquals(document, matchingDocument);
		}
	}

	private void SelectExplorerItemByContext(string context)
	{
		NavigationItemViewModel? matchingItem = ExplorerSections
			.SelectMany(section => section.Items)
			.FirstOrDefault(item => string.Equals(item.Context, context, StringComparison.OrdinalIgnoreCase));

		if (matchingItem is null)
		{
			return;
		}

		foreach (NavigationItemViewModel item in ExplorerSections.SelectMany(section => section.Items))
		{
			item.IsSelected = ReferenceEquals(item, matchingItem);
		}
	}

	private void ActivateRequestEditor()
	{
		_activeDocumentKind = RequestDocumentKind;
		ActivateCurrentCenterTabEditor();
	}

	private void ActivateSettingsEditor(NavigationItemViewModel item)
	{
		_activeDocumentKind = SettingsDocumentKind;
		_themeConfigText = ReadSettingsText(_currentThemeName);
		ActiveDocumentKindLabel = item.Kind;
		ActiveDocumentKindColor = item.AccentColor;
		ActiveDocumentLabel = item.Title;
		ActiveEditorLanguage = item.EditorLanguage;
		ActiveEditorEditableRangesJson = BuildEditableRangesJson(_themeConfigText);
		SetActiveEditorTextInternal(_themeConfigText);
		ForceActiveEditorRefresh();
		OnPropertyChanged(nameof(CanSend));
	}

	private void ActivateCurrentCenterTabEditor()
	{
		if (!IsActiveRequestEditor)
		{
			return;
		}

		string selectedTabKey = GetSelectedCenterTabKey();
		ActiveDocumentLabel = RequestDocumentLabel;
		ActiveDocumentKindLabel = selectedTabKey switch
		{
			"request" => SelectedMethod,
			"headers" => "HEADERS",
			"body" => _requestBodyMode == RequestBodyMode.Json ? "BODY JSON" : $"BODY {_requestBodyMode.ToString().ToUpperInvariant()}",
			"script" => "SCRIPT",
			"tests" => "TESTS",
			"variables" => "VARS",
			_ => SelectedMethod
		};
		ActiveDocumentKindColor = selectedTabKey switch
		{
			"script" => _warningColor,
			"tests" => _successColor,
			_ => SelectedMethodColor
		};
		ActiveEditorLanguage = selectedTabKey switch
		{
			"body" => _requestBodyMode == RequestBodyMode.Json ? "json" : "plaintext",
			"script" => "csharp",
			_ => "forrest"
		};
		ActiveEditorEditableRangesJson = "[]";
		SetActiveEditorTextInternal(selectedTabKey switch
		{
			"headers" => HeadersEditorText,
			"body" => BodyEditorText,
			"script" => ScriptEditorText,
			"tests" => TestsEditorText,
			"variables" => VariablesEditorText,
			_ => RequestEditorText
		});
		ForceActiveEditorRefresh();
		OnPropertyChanged(nameof(CanSend));
	}

	private string GetSelectedCenterTabKey()
	{
		return CenterTabs.FirstOrDefault(static tab => tab.IsSelected)?.Key ?? "request";
	}

	private void ApplyRequestEditorChange(string value)
	{
		string selectedTabKey = GetSelectedCenterTabKey();
		switch (selectedTabKey)
		{
			case "request":
				_requestEditorText = NormalizeLineEndings(value);
				SyncSupportEditorsFromRequestSource();
				break;
			case "headers":
				_headersEditorText = NormalizeLineEndings(value);
				_requestEditorText = _documentTextService.UpsertHeaders(_requestEditorText, _headersEditorText);
				break;
			case "body":
				_bodyEditorText = NormalizeLineEndings(value);
				_requestEditorText = _documentTextService.UpsertBody(_requestEditorText, _requestBodyMode, _bodyEditorText);
				break;
			case "script":
				_scriptEditorText = NormalizeLineEndings(value);
				break;
			case "tests":
				_testsEditorText = NormalizeLineEndings(value);
				_requestEditorText = _documentTextService.UpsertTests(_requestEditorText, _testsEditorText);
				break;
			case "variables":
				_variablesEditorText = NormalizeLineEndings(value);
				_requestEditorText = _documentTextService.UpsertVariables(_requestEditorText, _variablesEditorText);
				break;
			default:
				_requestEditorText = NormalizeLineEndings(value);
				break;
		}

		UpdateRequestMetadataFromSource();
		MarkCurrentDocumentDirty();
		if (!_suppressRequestAutosave)
		{
			ScheduleRequestAutosave();
		}
	}

	private void SyncSupportEditorsFromRequestSource()
	{
		if (_suppressDocumentSynchronization)
		{
			return;
		}

		_suppressDocumentSynchronization = true;
		try
		{
			ForRestScriptEditableSections sections = _documentTextService.Extract(_requestEditorText);
			_headersEditorText = sections.Headers;
			_bodyEditorText = sections.Body;
			_testsEditorText = sections.Tests;
			_variablesEditorText = sections.Variables;
			_requestBodyMode = sections.BodyMode;
			if (!string.IsNullOrWhiteSpace(sections.Name))
			{
				_requestName = sections.Name;
				OnPropertyChanged(nameof(RequestName));
				OnPropertyChanged(nameof(RequestDocumentLabel));
				OnPropertyChanged(nameof(RequestStateStatus));
				OnPropertyChanged(nameof(ActiveDocumentSummary));
				if (IsActiveRequestEditor)
				{
					ActiveDocumentLabel = RequestDocumentLabel;
				}
			}
		}
		finally
		{
			_suppressDocumentSynchronization = false;
		}
	}

	private void UpdateRequestMetadataFromSource()
	{
		ForRestScriptCompilationResult compilation = _scriptExecutionService.Compile(
			_requestEditorText,
			GetSelectedWorkspaceState()?.Id ?? HttpBinWorkspaceId,
			RequestName);
		if (!compilation.Succeeded || compilation.Payload is null)
		{
			ExecutionStatus = compilation.Diagnostics.Count == 0
				? "Editing request document"
				: string.Join("  ", compilation.Diagnostics.Take(3).Select(static diagnostic => $"L{diagnostic.Line}: {diagnostic.Message}"));
			RequestTarget = BuildDefaultRequestUrl(RequestLocation);
			return;
		}

		RequestName = compilation.Payload.Request.Name;
		SelectedMethod = compilation.Payload.Request.Method.ToString().ToUpperInvariant();
		RequestTarget = compilation.Payload.Request.UrlTemplate;
		RequestSummary = string.IsNullOrWhiteSpace(RequestSummary) ? $"{SelectedMethod} request" : RequestSummary;
		ExecutionStatus = "Request document ready";
		UpdateCurrentDocumentMetadata();
	}

	private void MarkCurrentDocumentDirty()
	{
		RequestDocumentViewModel? currentDocument = OpenDocuments.FirstOrDefault(static document => document.IsSelected);
		if (currentDocument is null)
		{
			return;
		}

		currentDocument.Title = RequestName;
		currentDocument.Method = SelectedMethod;
		currentDocument.Summary = RequestSummary;
		currentDocument.Location = RequestLocation;
		currentDocument.IsDirty = true;

		NavigationItemViewModel? explorerItem = ExplorerSections
			.SelectMany(static section => section.Items)
			.FirstOrDefault(item => string.Equals(item.Context, RequestLocation, StringComparison.OrdinalIgnoreCase));
		if (explorerItem is not null)
		{
			explorerItem.Title = RequestName;
			explorerItem.Detail = RequestSummary;
			explorerItem.AccentColor = ResolveMethodAccent(SelectedMethod);
		}
	}

	private async Task PersistCurrentRequestAsync(CancellationToken cancellationToken = default)
	{
		RequestWorkbenchDocumentState state = BuildCurrentDocumentState();
		UpdateSelectedWorkspaceState(
			workspace => workspace with
			{
				SelectedEnvironment = SelectedEnvironment,
				SelectedDocumentLocation = RequestLocation,
				Documents = UpsertDocument(workspace.Documents, state)
			});
		await _requestWorkbenchStateStore.SaveAsync(BuildWorkbenchState(), cancellationToken);

		RequestDocumentViewModel? currentDocument = OpenDocuments.FirstOrDefault(static document => document.IsSelected);
		if (currentDocument is not null)
		{
			currentDocument.IsDirty = false;
		}
	}

	private void ScheduleRequestAutosave()
	{
		CancellationTokenSource saveSource = new();
		CancellationTokenSource? previousSource = Interlocked.Exchange(ref _requestSaveSource, saveSource);
		previousSource?.Cancel();
		previousSource?.Dispose();

		_ = Task.Run(
			async () =>
			{
				try
				{
					await Task.Delay(600, saveSource.Token);
					await PersistCurrentRequestAsync(saveSource.Token);
				}
				catch (OperationCanceledException)
				{
				}
			});
	}

	private RequestWorkbenchDocumentState BuildCurrentDocumentState()
	{
		return new()
		{
			Title = RequestName,
			Method = SelectedMethod,
			Summary = RequestSummary,
			Location = RequestLocation,
			RequestSource = NormalizeLineEndings(RequestEditorText),
			PreRequestScript = NormalizeLineEndings(ScriptEditorText)
		};
	}

	private void UpdateCurrentDocumentMetadata()
	{
		RequestDocumentViewModel? currentDocument = OpenDocuments.FirstOrDefault(static document => document.IsSelected);
		if (currentDocument is not null)
		{
			currentDocument.Title = RequestName;
			currentDocument.Method = SelectedMethod;
			currentDocument.Summary = RequestSummary;
			currentDocument.Location = RequestLocation;
		}

		NavigationItemViewModel? explorerItem = ExplorerSections
			.SelectMany(static section => section.Items)
			.FirstOrDefault(item => string.Equals(item.Context, RequestLocation, StringComparison.OrdinalIgnoreCase));
		if (explorerItem is not null)
		{
			explorerItem.Title = RequestName;
			explorerItem.Detail = RequestSummary;
			explorerItem.AccentColor = ResolveMethodAccent(SelectedMethod);
		}
	}

	private void ApplyCompilationFailure(IReadOnlyList<ForRestScriptDiagnostic> diagnostics)
	{
		ResponseState = "Compile failed";
		ResponseBodyText = string.Empty;
		ResponseRawText = string.Empty;
		DebugOutputText = string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => $"Line {diagnostic.Line}, Col {diagnostic.Column}: {diagnostic.Message}"));
		_responseTimeStatus = "--";
		_responseSizeStatus = "--";
		ExecutionStatus = diagnostics.Count == 0
			? "Request document failed to compile"
			: diagnostics[0].Message;
		ResponseHeaderRows.Clear();
		TraceEntries.Clear();
		TraceEntries.Add(new TraceEntryViewModel("compile", ExecutionStatus, DateTime.Now.ToString("T"), _dangerColor));
		OutputMetrics.Clear();
		OutputMetrics.Add(new OutputMetricViewModel("Status", "Compile error", _dangerColor));
		OnPropertyChanged(nameof(ResponseTimeStatus));
		OnPropertyChanged(nameof(ResponseSizeStatus));
		FocusRightPaneTab("debug");
	}

	private void PersistActiveRequestInBackground()
	{
		if (!_isInitialized || !IsActiveRequestEditor || string.IsNullOrWhiteSpace(RequestLocation))
		{
			return;
		}

		_ = Task.Run(
			async () =>
			{
				try
				{
					await PersistCurrentRequestAsync();
				}
				catch
				{
				}
			});
	}

	private AppProfile BuildProfile()
	{
		RequestWorkbenchWorkspaceState? workspace = GetSelectedWorkspaceState();
		return new()
		{
			GlobalVariables =
			[
				new VariableDefinition { Key = "app_name", Value = "ForRest", Scope = VariableScope.Global },
				new VariableDefinition { Key = "workspace_id", Value = (workspace?.Id ?? HttpBinWorkspaceId).ToString(), Scope = VariableScope.Global }
			]
		};
	}

	private WorkspaceSnapshot BuildWorkspaceSnapshot()
	{
		RequestWorkbenchWorkspaceState workspaceState = GetSelectedWorkspaceState() ?? BuildUserWorkspace("Workspace");
		return new()
		{
			Workspace = new()
			{
				Id = workspaceState.Id,
				Name = SelectedWorkspace,
				Variables =
				[
					new VariableDefinition { Key = "workspace_name", Value = SelectedWorkspace, Scope = VariableScope.Workspace },
					new VariableDefinition { Key = "base_url", Value = BuildBaseUrl(RequestTarget), Scope = VariableScope.Workspace }
				]
			},
			Environments = BuildEnvironmentDefinition() is { } environment ? [environment] : []
		};
	}

	private EnvironmentDefinition BuildEnvironmentDefinition()
	{
		RequestWorkbenchWorkspaceState workspaceState = GetSelectedWorkspaceState() ?? BuildUserWorkspace("Workspace");
		return new()
		{
			WorkspaceId = workspaceState.Id,
			Name = SelectedEnvironment,
			IsActive = true,
			Variables =
			[
				new VariableDefinition { Key = "environment_name", Value = SelectedEnvironment, Scope = VariableScope.Environment }
			]
		};
	}

	private void SetActiveEditorTextInternal(string value)
	{
		_activeEditorText = value;
		OnPropertyChanged(nameof(ActiveEditorText));
	}

	private void ForceActiveEditorRefresh()
	{
		OnPropertyChanged(nameof(ActiveEditorLanguage));
		OnPropertyChanged(nameof(ActiveEditorEditableRangesJson));
		OnPropertyChanged(nameof(ActiveEditorText));
	}

	private void ScheduleSettingsAutosave()
	{
		if (!_settingsTomlDocumentService.CanAutoSave(_themeConfigText))
		{
			return;
		}

		CancellationTokenSource saveSource = new();
		CancellationTokenSource? previousSource = Interlocked.Exchange(ref _settingsSaveSource, saveSource);
		previousSource?.Cancel();
		previousSource?.Dispose();

		string pendingText = _themeConfigText;

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(600, saveSource.Token);
				_settingsTomlDocumentService.SaveRawText(pendingText);
			}
			catch (OperationCanceledException)
			{
			}
		});
	}

	private void UpdateSettingsTextFromDisk(ShellThemeName currentTheme)
	{
		string latestText = ReadSettingsText(currentTheme);
		_themeConfigText = latestText;

		if (!IsActiveSettingsEditor)
		{
			return;
		}

		_suppressSettingsAutosave = true;
		try
		{
			ActiveEditorEditableRangesJson = BuildEditableRangesJson(latestText);
			SetActiveEditorTextInternal(latestText);
		}
		finally
		{
			_suppressSettingsAutosave = false;
		}
	}

	private string ReadSettingsText(ShellThemeName currentTheme)
	{
		return _settingsTomlDocumentService.LoadOrCreate(new ForRestSettings(currentTheme));
	}

	private string BuildEditableRangesJson(string text)
	{
		return JsonSerializer.Serialize(
			_settingsTomlDocumentService.GetEditableRanges(text),
			new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
	}

	private double ClampLeftPane(double requestedWidth, double totalWidth)
	{
		double right = _rightPaneCollapsed ? 0d : _rightPanePixels;
		double splitters = (_leftPaneCollapsed ? 0d : SplitterPixels) + (_rightPaneCollapsed ? 0d : SplitterPixels);
		double max = Math.Max(MinLeftPanePixels, totalWidth - right - splitters - MinCenterPanePixels);
		return Math.Clamp(requestedWidth, MinLeftPanePixels, max);
	}

	private double ClampRightPane(double requestedWidth, double totalWidth)
	{
		double left = _leftPaneCollapsed ? 0d : _leftPanePixels;
		double splitters = (_leftPaneCollapsed ? 0d : SplitterPixels) + (_rightPaneCollapsed ? 0d : SplitterPixels);
		double max = Math.Max(MinRightPanePixels, totalWidth - left - splitters - MinCenterPanePixels);
		return Math.Clamp(requestedWidth, MinRightPanePixels, max);
	}

	private void NotifyPaneLayoutChanged()
	{
		OnPropertyChanged(nameof(IsLeftPaneVisible));
		OnPropertyChanged(nameof(IsRightPaneVisible));
		OnPropertyChanged(nameof(LeftPaneWidth));
		OnPropertyChanged(nameof(RightPaneWidth));
		OnPropertyChanged(nameof(LeftSplitterWidth));
		OnPropertyChanged(nameof(RightSplitterWidth));
		OnPropertyChanged(nameof(ShowLeftPaneRestoreButton));
		OnPropertyChanged(nameof(ShowRightPaneRestoreButton));
	}

	private void NotifyOverlayChanged()
	{
		OnPropertyChanged(nameof(IsExplorerOverlayVisible));
		OnPropertyChanged(nameof(IsInspectorOverlayVisible));
		OnPropertyChanged(nameof(IsOverlayBackdropVisible));
	}

	private string BuildDebugOutput(ForRestScriptExecutionOutcome outcome)
	{
		List<string> lines =
		[
			$"Workspace: {SelectedWorkspace}",
			$"Environment: {SelectedEnvironment}",
			$"Request: {SelectedMethod} {RequestName}",
			$"Target: {RequestTarget}"
		];

		if (outcome.Compilation.Diagnostics.Count > 0)
		{
			lines.Add(string.Empty);
			lines.Add("Compilation diagnostics:");
			lines.AddRange(outcome.Compilation.Diagnostics.Select(static diagnostic => $"  L{diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}"));
		}

		if (outcome.Execution is null)
		{
			return string.Join(Environment.NewLine, lines);
		}

		ExecutionRun? latestRun = outcome.Execution.Runs.LastOrDefault();
		lines.Add(string.Empty);
		lines.Add($"Execution state: {outcome.Execution.State}");
		if (latestRun?.Response is { } response)
		{
			lines.Add($"Response: {response.StatusCode} {response.ReasonPhrase}".Trim());
			lines.Add($"Duration: {response.DurationMilliseconds} ms");
			lines.Add($"Size: {FormatResponseSize(response.SizeBytes)}");
		}

		if (!string.IsNullOrWhiteSpace(latestRun?.ErrorMessage))
		{
			lines.Add($"Error: {latestRun.ErrorMessage}");
		}

		if (outcome.Execution.ConsoleEntries.Count > 0)
		{
			lines.Add(string.Empty);
			lines.Add("Console:");
			lines.AddRange(outcome.Execution.ConsoleEntries.Select(entry => $"  [{entry.Level}] {entry.Message}"));
		}

		if (outcome.Execution.Tests.Count > 0)
		{
			lines.Add(string.Empty);
			lines.Add("Tests:");
			lines.AddRange(outcome.Execution.Tests.Select(entry => $"  [{entry.State}] {entry.Name}"));
		}

		return string.Join(Environment.NewLine, lines);
	}

	private void FocusRightPaneTab(string key)
	{
		PaneTabViewModel? tab = RightPaneTabs.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
		if (tab is not null)
		{
			SelectRightPaneTab(tab);
		}
	}

	private static string BuildBaseUrl(string target)
	{
		if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
		{
			return uri.GetLeftPart(UriPartial.Authority);
		}

		return target;
	}

	private static List<RequestWorkbenchWorkspaceState> BuildDefaultWorkspaces()
	{
		return
		[
			BuildWorkspace(
				HttpBinWorkspaceId,
				"HTTP Bin Playground",
				[
					BuildDefaultDocumentState(
						"Get Headers",
						"GET",
						"Inspect request headers round-trip",
						"/requests/httpbin/headers",
						"https://httpbin.org/headers",
						tests:
						[
							"status == 200 \"returns 200\"",
							"header \"Content-Type\" contains \"json\" \"json response\""
						]),
					BuildDefaultDocumentState(
						"Post Echo",
						"POST",
						"Echo a JSON payload through httpbin",
						"/requests/httpbin/post",
						"https://httpbin.org/post",
						bodyContent: BuildBodyEditorText("Post Echo"),
						tests:
						[
							"status == 200 \"returns 200\"",
							"header \"Content-Type\" contains \"json\" \"json response\""
						]),
					BuildDefaultDocumentState(
						"Get UUID",
						"GET",
						"Fetch a generated identifier",
						"/requests/httpbin/uuid",
						"https://httpbin.org/uuid",
						tests:
						[
							"status == 200 \"returns 200\"",
							"body contains \"uuid\" \"uuid field exists\""
						])
				]),
			BuildWorkspace(
				JsonPlaceholderWorkspaceId,
				"JSON Placeholder",
				[
					BuildDefaultDocumentState(
						"Get Todo",
						"GET",
						"Read a simple todo resource",
						"/requests/jsonplaceholder/todo",
						"https://jsonplaceholder.typicode.com/todos/1",
						tests:
						[
							"status == 200 \"returns 200\"",
							"header \"Content-Type\" contains \"json\" \"json response\""
						]),
					BuildDefaultDocumentState(
						"Create Post",
						"POST",
						"Create a sample post payload",
						"/requests/jsonplaceholder/post",
						"https://jsonplaceholder.typicode.com/posts",
						bodyContent:
						"""
						{
						  "title": "for-rest starter",
						  "body": "Created from the MAUI workbench",
						  "userId": 7
						}
						""",
						tests:
						[
							"status == 201 \"returns 201\"",
							"header \"Content-Type\" contains \"json\" \"json response\""
						]),
					BuildDefaultDocumentState(
						"List Users",
						"GET",
						"Fetch a simple collection response",
						"/requests/jsonplaceholder/users",
						"https://jsonplaceholder.typicode.com/users",
						tests:
						[
							"status == 200 \"returns 200\"",
							"header \"Content-Type\" contains \"json\" \"json response\""
						])
				])
		];
	}

	private static RequestWorkbenchWorkspaceState BuildWorkspace(
		Guid id,
		string name,
		IReadOnlyList<RequestWorkbenchDocumentState> documents)
	{
		return new()
		{
			Id = id,
			Name = name,
			SelectedEnvironment = "Local",
			SelectedDocumentLocation = documents.FirstOrDefault()?.Location ?? string.Empty,
			Documents = [.. documents]
		};
	}

	private static RequestWorkbenchDocumentState BuildDefaultDocumentState(string title, string method, string summary, string location)
	{
		return BuildDefaultDocumentState(
			title,
			method,
			summary,
			location,
			BuildDefaultRequestUrl(location),
			bodyContent: string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) ? BuildBodyEditorText(title) : null);
	}

	private static RequestWorkbenchDocumentState BuildDefaultDocumentState(
		string title,
		string method,
		string summary,
		string location,
		string target,
		string? bodyContent = null,
		IReadOnlyList<string>? tests = null,
		IReadOnlyList<string>? variableLines = null,
		IReadOnlyList<string>? scriptLines = null)
	{
		return new()
		{
			Title = title,
			Method = method,
			Summary = summary,
			Location = location,
			RequestSource = BuildRequestEditorText(title, method, target, bodyContent, tests, variableLines),
			PreRequestScript = BuildScriptEditorText(title, scriptLines)
		};
	}

	private static string BuildDefaultRequestUrl(string location)
	{
		return $"https://httpbin.org/anything?source={Uri.EscapeDataString(location)}";
	}

	private static string BuildRequestDocumentLabel(string title)
	{
		if (string.IsNullOrWhiteSpace(title))
		{
			return "request.frs";
		}

		char[] slugCharacters = title
			.ToLowerInvariant()
			.Select(character => char.IsLetterOrDigit(character) ? character : '-')
			.ToArray();

		string slug = string.Join(
			"-",
			new string(slugCharacters)
				.Split('-', StringSplitOptions.RemoveEmptyEntries));

		return string.IsNullOrWhiteSpace(slug) ? "request.frs" : $"{slug}.frs";
	}

	private static string BuildRequestEditorText(
		string title,
		string method,
		string target,
		string? bodyContent = null,
		IReadOnlyList<string>? tests = null,
		IReadOnlyList<string>? variableLines = null)
	{
		List<string> lines =
		[
			"meta {",
			$"  name = \"{title}\"",
			"}",
			string.Empty,
			"vars {"
		];

		foreach (string variableLine in variableLines ?? ["runtime trace_id = guid()"])
		{
			lines.Add($"  {variableLine}");
		}

		lines.Add("}");
		lines.Add(string.Empty);
		lines.Add("request {");
		lines.Add($"  method = {method}");
		lines.Add($"  url = \"{target}\"");
		lines.Add("  timeout = 15000");
		lines.Add("  redirects = true");
		lines.Add("  ssl = true");
		lines.Add("  history = true");
		if (!string.IsNullOrWhiteSpace(bodyContent))
		{
			lines.Add("  content_type = \"application/json\"");
		}

		lines.Add("}");
		lines.Add(string.Empty);
		lines.Add("headers {");
		lines.Add("  Accept = \"application/json\"");
		lines.Add("  X-Workspace = \"{{workspace_name}}\"");
		lines.Add("  X-Environment = \"{{environment_name}}\"");
		lines.Add("  X-Correlation-Id = \"{{trace_id}}\"");
		lines.Add("}");

		if (!string.IsNullOrWhiteSpace(bodyContent))
		{
			lines.Add(string.Empty);
			lines.Add("body json \"\"\"");
			lines.AddRange(NormalizeLineEndings(bodyContent).Split('\n'));
			lines.Add("\"\"\"");
		}

		lines.Add(string.Empty);
		lines.Add("tests {");
		foreach (string testLine in tests ?? ["status == 200 \"returns 200\"", "header \"Content-Type\" contains \"json\" \"json response\""])
		{
			lines.Add($"  {testLine}");
		}

		lines.Add("}");
		return string.Join(Environment.NewLine, lines);
	}

	private static string BuildHeadersEditorText()
	{
		return string.Join(
			Environment.NewLine,
			[
				"Accept = \"application/json\"",
				"X-Workspace = \"{{workspace_name}}\"",
				"X-Environment = \"{{environment_name}}\"",
				"X-Correlation-Id = \"{{trace_id}}\""
			]);
	}

	private static string BuildBodyEditorText(string title)
	{
		return string.Join(
			Environment.NewLine,
			[
				"{",
				$"  \"request\": \"{title}\",",
				"  \"createdBy\": \"for-rest\"",
				"}"
			]);
	}

	private static string BuildScriptEditorText(string title, IReadOnlyList<string>? scriptLines = null)
	{
		return string.Join(
			Environment.NewLine,
			scriptLines
			??
			[
				$"variables.Set(\"request_name\", \"{title}\");",
				"request.SetHeader(\"X-Request-Source\", \"maui\");",
				"console.Log(\"Prepared request before send.\");"
			]);
	}

	private static string BuildTestsEditorText()
	{
		return string.Join(
			Environment.NewLine,
			[
				"status == 200 \"returns 200\"",
				"header \"Content-Type\" contains \"json\" \"json response\""
			]);
	}

	private static string BuildVariablesEditorText()
	{
		return string.Join(
			Environment.NewLine,
			[
				"request resource_id = \"42\"",
				"runtime trace_id = guid()"
			]);
	}

	private static string NormalizeLineEndings(string text)
	{
		return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
	}

	private static string FormatResponseSize(long sizeBytes)
	{
		return sizeBytes switch
		{
			>= 1_048_576 => $"{sizeBytes / 1_048_576d:0.##} MB",
			>= 1_024 => $"{sizeBytes / 1_024d:0.##} KB",
			_ => $"{sizeBytes} B"
		};
	}
}
