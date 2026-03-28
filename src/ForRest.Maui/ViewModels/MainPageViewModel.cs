using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using ForRest.Models;
using ForRest.Repositories;
using ForRest.Services;
using ForRest.Scripting;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

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
	private readonly IThemeService _themeService;
	private readonly SettingsTomlDocumentService _settingsTomlDocumentService;
	private readonly RequestWorkbenchStateStore _requestWorkbenchStateStore;
	private readonly IForRestScriptExecutionService _scriptExecutionService;
	private readonly IExecutionHistoryRepository _executionHistoryRepository;
	private readonly ForRestScriptDocumentTextService _documentTextService;
	private readonly IAppActivationService _appActivationService;
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
	private ResponseSnapshot? _latestResponseSnapshot;
	private bool _isResponsePrettyPrintEnabled = true;
	private string _debugOutputText;
	private string _executionStatus;
	private string _activationStatus;
	private string _activationDetail;
	private bool _canExecuteRequests = true;
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
	private string _activeEditorDiagnosticsJson;
	private string _requestEditorDiagnosticsJson;
	private string _editorDebugStateText;
	private string _editorDebugSummaryText;
	private string _editorDebugDetailText;
	private Color _editorDebugAccentColor;
	private CancellationTokenSource? _settingsSaveSource;
	private CancellationTokenSource? _requestSaveSource;
	private CancellationTokenSource? _requestMetadataRefreshSource;
	private int _requestMetadataRefreshVersion;
	private bool _suppressSettingsAutosave;
	private bool _suppressRequestAutosave;
	private bool _suppressDocumentSynchronization;
	private bool _isInitialized;
	private bool _isSending;
	private RequestBodyMode _requestBodyMode = RequestBodyMode.Json;
	private Guid _selectedWorkspaceId;
	private readonly IReadOnlyList<LanguageHelpEntryViewModel> _languageHelpSourceEntries;
	private string _languageHelpCatalogJson;
	private bool _isLanguageHelpOpen;
	private string _languageHelpSearchText;
	private string _selectedLanguageHelpKey;
	private string _selectedLanguageHelpTitle;
	private string _selectedLanguageHelpCategory;
	private string _selectedLanguageHelpSummary;
	private string _selectedLanguageHelpDocumentation;
	private string _selectedLanguageHelpExample;
	private int _activeEditorLineNumber = 1;

	public MainPageViewModel(
		IThemeService themeService,
		SettingsTomlDocumentService settingsTomlDocumentService,
		RequestWorkbenchStateStore requestWorkbenchStateStore,
		IForRestScriptExecutionService scriptExecutionService,
		IExecutionHistoryRepository executionHistoryRepository,
		ForRestScriptDocumentTextService documentTextService,
		IAppActivationService appActivationService)
	{
		_themeService = themeService;
		_settingsTomlDocumentService = settingsTomlDocumentService;
		_requestWorkbenchStateStore = requestWorkbenchStateStore;
		_scriptExecutionService = scriptExecutionService;
		_executionHistoryRepository = executionHistoryRepository;
		_documentTextService = documentTextService;
		_appActivationService = appActivationService;
		_languageHelpSourceEntries =
		[
			.. ForRestLanguageCatalog.GetEntries().Select(
				entry => new LanguageHelpEntryViewModel(
					entry.Key,
					entry.Title,
					entry.Category,
					entry.Summary,
					entry.Documentation,
					entry.Example,
					entry.SearchTerms))
		];
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
		_requestEditorText = NormalizeLineEndings(RequestWorkbenchDocumentNormalizer.NormalizeRequestDocumentSource(starterDocument.RequestSource, starterDocument.PreRequestScript, starterDocument.Title));
		_headersEditorText = string.Empty;
		_bodyEditorText = string.Empty;
		_scriptEditorText = string.Empty;
		_testsEditorText = string.Empty;
		_variablesEditorText = string.Empty;
		_responseBodyText = string.Empty;
		_responseRawText = string.Empty;
		_responseState = "Idle";
		_responseTimeStatus = "--";
		_responseSizeStatus = "--";
		_debugOutputText = "Debug output, compile diagnostics, console entries, and exceptions appear here.";
		_executionStatus = themeService.CurrentStatusMessage;
		_activationStatus = "Activation pending";
		_activationDetail = "License state has not been evaluated yet.";
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
		_activeEditorDiagnosticsJson = "[]";
		_requestEditorDiagnosticsJson = "[]";
		_editorDebugStateText = "Ready";
		_editorDebugSummaryText = "POST Starter Request";
		_editorDebugDetailText = "https://httpbin.org/anything  send<=3  vars 0  tests 0  extracts 0";
		_editorDebugAccentColor = _successColor;
		_languageHelpCatalogJson = ForRestLanguageCatalog.BuildMonacoCatalogJson();
		_languageHelpSearchText = string.Empty;
		_selectedLanguageHelpKey = string.Empty;
		_selectedLanguageHelpTitle = string.Empty;
		_selectedLanguageHelpCategory = string.Empty;
		_selectedLanguageHelpSummary = string.Empty;
		_selectedLanguageHelpDocumentation = string.Empty;
		_selectedLanguageHelpExample = string.Empty;

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
			new PaneTabViewModel("request", "Request", true)
		];

		RightPaneTabs =
		[
			new PaneTabViewModel("response", "Response", true),
			new PaneTabViewModel("stash", "Stash"),
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

		StashColumns =
		[];

		StashRows =
		[];

		TraceEntries =
		[
			new TraceEntryViewModel("ready", "request workbench initialized", DateTime.Now.ToString("T"), _methodNeutral)
		];

		LanguageHelpEntries =
		[];

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
		RefreshLanguageHelpEntries();
		ActivateRequestEditor();
		SyncSupportEditorsFromRequestSource();
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

	public ObservableCollection<StashColumnViewModel> StashColumns { get; }

	public ObservableCollection<StashRowViewModel> StashRows { get; }

	public ObservableCollection<LanguageHelpEntryViewModel> LanguageHelpEntries { get; }

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
				if (IsActiveRequestEditor)
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
				OnPropertyChanged(nameof(CanMoveRequestUp));
				OnPropertyChanged(nameof(CanMoveRequestDown));
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
		set
		{
			if (SetProperty(ref _responseBodyText, value))
			{
				OnPropertyChanged(nameof(CanCopyResponseBody));
			}
		}
	}

	public string ResponseRawText
	{
		get => _responseRawText;
		set
		{
			if (SetProperty(ref _responseRawText, value))
			{
				OnPropertyChanged(nameof(CanCopyRawResponse));
			}
		}
	}

	public bool IsResponsePrettyPrintEnabled
	{
		get => _isResponsePrettyPrintEnabled;
		set
		{
			if (SetProperty(ref _isResponsePrettyPrintEnabled, value))
			{
				OnPropertyChanged(nameof(ResponsePrettyPrintButtonText));
				RefreshResponsePresentation();
			}
		}
	}

	public string ResponsePrettyPrintButtonText => IsResponsePrettyPrintEnabled ? "Pretty JSON: On" : "Pretty JSON: Off";

	public string DebugOutputText
	{
		get => _debugOutputText;
		set
		{
			if (SetProperty(ref _debugOutputText, value))
			{
				OnPropertyChanged(nameof(CanCopyDebugOutput));
			}
		}
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
					if (_settingsTomlDocumentService.CanAutoSave(_themeConfigText))
					{
						_themeService.PreviewConfigText(_themeConfigText);
					}

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
		set
		{
			if (SetProperty(ref _activeEditorLanguage, value))
			{
				OnPropertyChanged(nameof(IsLanguageHelpAvailable));
				OnPropertyChanged(nameof(ShowLanguageHelpToggle));
				OnPropertyChanged(nameof(ShowLanguageHelpDrawer));
				OnPropertyChanged(nameof(ShowInlineLanguageHelpDrawer));
				OnPropertyChanged(nameof(ShowCompactLanguageHelpDrawer));
			}
		}
	}

	public string ActiveEditorEditableRangesJson
	{
		get => _activeEditorEditableRangesJson;
		set => SetProperty(ref _activeEditorEditableRangesJson, value);
	}

	public string ActiveEditorDiagnosticsJson
	{
		get => _activeEditorDiagnosticsJson;
		set => SetProperty(ref _activeEditorDiagnosticsJson, value);
	}

	public string LanguageHelpCatalogJson => _languageHelpCatalogJson;

	public bool IsLanguageHelpOpen
	{
		get => _isLanguageHelpOpen;
		set
		{
			if (SetProperty(ref _isLanguageHelpOpen, value))
			{
				OnPropertyChanged(nameof(LanguageHelpToggleText));
				OnPropertyChanged(nameof(ShowLanguageHelpDrawer));
				OnPropertyChanged(nameof(ShowInlineLanguageHelpDrawer));
				OnPropertyChanged(nameof(ShowCompactLanguageHelpDrawer));
			}
		}
	}

	public bool IsLanguageHelpAvailable => IsActiveRequestEditor && string.Equals(ActiveEditorLanguage, "forrest", StringComparison.Ordinal);

	public bool ShowLanguageHelpToggle => IsLanguageHelpAvailable;

	public bool ShowLanguageHelpDrawer => IsLanguageHelpAvailable && IsLanguageHelpOpen;

	public bool ShowInlineLanguageHelpDrawer => ShowLanguageHelpDrawer && IsDesktopLayout;

	public bool ShowCompactLanguageHelpDrawer => ShowLanguageHelpDrawer && IsCompactLayout;

	public string LanguageHelpToggleText => IsLanguageHelpOpen ? "Docs -" : "Docs +";

	public string LanguageHelpSearchText
	{
		get => _languageHelpSearchText;
		set
		{
			if (SetProperty(ref _languageHelpSearchText, value))
			{
				RefreshLanguageHelpEntries();
			}
		}
	}

	public string SelectedLanguageHelpTitle
	{
		get => _selectedLanguageHelpTitle;
		set => SetProperty(ref _selectedLanguageHelpTitle, value);
	}

	public string SelectedLanguageHelpCategory
	{
		get => _selectedLanguageHelpCategory;
		set => SetProperty(ref _selectedLanguageHelpCategory, value);
	}

	public string SelectedLanguageHelpSummary
	{
		get => _selectedLanguageHelpSummary;
		set => SetProperty(ref _selectedLanguageHelpSummary, value);
	}

	public string SelectedLanguageHelpDocumentation
	{
		get => _selectedLanguageHelpDocumentation;
		set => SetProperty(ref _selectedLanguageHelpDocumentation, value);
	}

	public string SelectedLanguageHelpExample
	{
		get => _selectedLanguageHelpExample;
		set => SetProperty(ref _selectedLanguageHelpExample, value);
	}

	public bool HasSelectedLanguageHelpEntry => !string.IsNullOrWhiteSpace(SelectedLanguageHelpTitle);

	public bool ShowLanguageHelpEmptyState => !HasSelectedLanguageHelpEntry;

	public bool CanCopyLanguageHelpExample => !string.IsNullOrWhiteSpace(SelectedLanguageHelpExample);

	public string LanguageHelpEmptyStateText => LanguageHelpEntries.Count == 0
		? "No ForRest topics matched the current search."
		: "Select a topic to inspect syntax, examples, and supported helpers.";

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

	public bool ShowEditorDebugStrip => IsActiveRequestEditor;

	public string EditorDebugStateText
	{
		get => _editorDebugStateText;
		set => SetProperty(ref _editorDebugStateText, value);
	}

	public string EditorDebugSummaryText
	{
		get => _editorDebugSummaryText;
		set => SetProperty(ref _editorDebugSummaryText, value);
	}

	public string EditorDebugDetailText
	{
		get => _editorDebugDetailText;
		set => SetProperty(ref _editorDebugDetailText, value);
	}

	public Color EditorDebugAccentColor
	{
		get => _editorDebugAccentColor;
		set => SetProperty(ref _editorDebugAccentColor, value);
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
			double available = Math.Max(0d, _workbenchWidth - 24d);
			if (available <= 0d)
			{
				return CompactPaneMinWidth;
			}

			return Math.Min(CompactPaneMaxWidth, available);
		}
	}

	public bool ShowCompactActionBar => _isCompactLayout;

	public bool ShowDesktopStatusBar => !_isCompactLayout;

	public bool ShowCompactStatusBar => _isCompactLayout;

	public string CompactLeftPaneButtonText => IsExplorerOverlayVisible ? "Close Explorer" : "Explorer";

	public string CompactRightPaneButtonText => IsInspectorOverlayVisible ? "Close Inspect" : "Inspect";

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

	public string ActivationStatus
	{
		get => _activationStatus;
		set => SetProperty(ref _activationStatus, value);
	}

	public string ActivationDetail
	{
		get => _activationDetail;
		set => SetProperty(ref _activationDetail, value);
	}

	public string ResponseSizeStatus => _responseSizeStatus;

	public string ResponseTimeStatus => _responseTimeStatus;

	public bool ShowLeftPaneRestoreButton => !_isCompactLayout && _leftPaneCollapsed;

	public bool ShowRightPaneRestoreButton => !_isCompactLayout && _rightPaneCollapsed;

	public bool IsSending
	{
		get => _isSending;
		private set
		{
			if (SetProperty(ref _isSending, value))
			{
				OnPropertyChanged(nameof(CanSend));
				OnPropertyChanged(nameof(SendButtonText));
			}
		}
	}

	public bool CanSend => !IsSending && IsActiveRequestEditor && _canExecuteRequests;

	public string SendButtonText => IsSending ? string.Empty : "\u25B6";

	public bool CanMoveWorkspaceLeft => GetSelectedWorkspaceIndex() > 0;

	public bool CanMoveWorkspaceRight
	{
		get
		{
			int index = GetSelectedWorkspaceIndex();
			return index >= 0 && index < Workspaces.Count - 1;
		}
	}

	public bool CanMoveRequestUp => IsActiveRequestEditor && GetSelectedRequestIndex() > 0;

	public bool CanMoveRequestDown
	{
		get
		{
			if (!IsActiveRequestEditor)
			{
				return false;
			}

			RequestWorkbenchWorkspaceState? workspace = GetSelectedWorkspaceState();
			int index = GetSelectedRequestIndex();
			return workspace is not null && index >= 0 && index < workspace.Documents.Count - 1;
		}
	}

	public bool CanDeleteWorkspace => GetSelectedWorkspaceState() is not null;

	public bool CanDeleteRequest => IsActiveRequestEditor && GetSelectedRequestIndex() >= 0;

	public bool HasStashData => StashColumns.Count > 0 && StashRows.Count > 0;

	public bool ShowStashEmptyState => !HasStashData;

	public bool CanCopyResponseBody => !string.IsNullOrWhiteSpace(ResponseBodyText);

	public bool CanCopyRawResponse => !string.IsNullOrWhiteSpace(ResponseRawText);

	public bool CanCopyDebugOutput => !string.IsNullOrWhiteSpace(DebugOutputText);

	public bool CanCopyHeaders => ResponseHeaderRows.Count > 0;

	public bool CanCopyTrace => TraceEntries.Count > 0;

	public bool CanCopyStash => HasStashData;

	public bool CanExportStashCsv => HasStashData;

	public string StashEmptyStateText => "No stash rows were captured for the current run. Flow code must execute stash writes before the run ends; lines skipped by break, continue, or return do not contribute rows.";

	public Color SelectedMethodColor => SelectedMethod switch
	{
		"GET" => _methodGet,
		"POST" => _methodPost,
		"PUT" => _methodPut,
		"DELETE" => _methodDelete,
		_ => _methodNeutral
	};

	public string CenterSurfaceStatus => "FRS program editor surface";

	public string RightSurfaceStatus => RightPaneTabs.FirstOrDefault(tab => tab.IsSelected)?.Key switch
	{
		"response" => "Primary response viewer",
		"stash" => "Structured stash table",
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

	public bool IsInspectorStashVisible => IsTabSelected(RightPaneTabs, "stash");

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
		state = RequestWorkbenchDocumentNormalizer.NormalizeLoadedWorkbenchState(state, out bool stateWasNormalized);
		if (stateWasNormalized)
		{
			await _requestWorkbenchStateStore.SaveAsync(state);
		}

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
		await ReloadHistoryAsync();
		RefreshActivationStatus();
		_isInitialized = true;
	}

	public async Task SendAsync()
	{
		if (!IsActiveRequestEditor || IsSending)
		{
			return;
		}

		RefreshActivationStatus();
		if (!_canExecuteRequests)
		{
			ApplyActivationBlock();
			return;
		}

		IsSending = true;
		try
		{
			string executionSource = NormalizeCurrentRequestEditorSource(applyToEditor: true);
			await PersistCurrentRequestAsync();
			AppProfile profile = BuildProfile();
			WorkspaceSnapshot workspaceSnapshot = BuildWorkspaceSnapshot();
			EnvironmentDefinition? environment = BuildEnvironmentDefinition();
			string requestName = RequestName;
			ForRestScriptExecutionOutcome outcome = await Task.Run(
				() => _scriptExecutionService.Execute(
					profile,
					workspaceSnapshot,
					executionSource,
					environment,
					requestName));

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
			ExecutionRun? latestRun = outcome.Execution?.Runs.LastOrDefault();
			ResponseState = outcome.Execution?.LatestResponse is { } response
				? $"{response.StatusCode} {response.ReasonPhrase}".Trim()
				: outcome.Execution?.State.ToString() ?? "Compiled";
			_latestResponseSnapshot = outcome.Execution?.LatestResponse;
			RefreshResponsePresentation();
			_responseTimeStatus = outcome.Execution?.LatestResponse is { } latestResponse
				? $"{latestResponse.DurationMilliseconds} ms"
				: "--";
			_responseSizeStatus = outcome.Execution?.LatestResponse is { } latestSizeResponse
				? FormatResponseSize(latestSizeResponse.SizeBytes)
				: "--";
			ExecutionStatus = outcome.Execution?.State == ExecutionState.Completed
				? "Ran request script"
				: outcome.Execution?.State == ExecutionState.Failed
					? "Execution failed"
					: "Compiled request document";
			DebugOutputText = BuildDebugOutput(outcome);

			ResponseHeaderRows.Clear();
			foreach (KeyValueDefinition header in outcome.Execution?.LatestResponse?.Headers ?? [])
			{
				ResponseHeaderRows.Add(new NameValueRowViewModel(header.Key, header.Value, "response"));
			}
			OnPropertyChanged(nameof(CanCopyHeaders));

			ApplyStashTable(latestRun?.Stash ?? outcome.Execution?.Stash ?? new());

			await ReloadHistoryAsync(latestRun?.Id);

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
			OnPropertyChanged(nameof(CanCopyTrace));

			OutputMetrics.Clear();
			OutputMetrics.Add(new OutputMetricViewModel("Status", ResponseState, ResponseState.StartsWith("2", StringComparison.Ordinal) ? _successColor : _dangerColor));
			OutputMetrics.Add(new OutputMetricViewModel("Time", _responseTimeStatus, SelectedMethodColor));
			OutputMetrics.Add(new OutputMetricViewModel("Size", _responseSizeStatus, _methodNeutral));
			OutputMetrics.Add(new OutputMetricViewModel("Type", outcome.Execution?.LatestResponse?.ContentType ?? "n/a", SelectedMethodColor));
			OnPropertyChanged(nameof(ResponseTimeStatus));
			OnPropertyChanged(nameof(ResponseSizeStatus));

			if (_isCompactLayout)
			{
				FocusRightPaneTab(outcome.Execution?.State == ExecutionState.Failed ? "debug" : "response");
				RevealInspectorOnCompactLayout();
			}
			else if (outcome.Execution?.State == ExecutionState.Failed)
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
			_latestResponseSnapshot = null;
			ResponseBodyText = string.Empty;
			ResponseRawText = string.Empty;
			DebugOutputText = exception.ToString();
			ResponseHeaderRows.Clear();
			OnPropertyChanged(nameof(CanCopyHeaders));
			ClearStashTable();
			OutputMetrics.Clear();
			OutputMetrics.Add(new OutputMetricViewModel("Status", "Failed", _dangerColor));
			OutputMetrics.Add(new OutputMetricViewModel("Time", "--", _methodNeutral));
			OutputMetrics.Add(new OutputMetricViewModel("Size", "--", _methodNeutral));
			OutputMetrics.Add(new OutputMetricViewModel("Type", "n/a", _methodNeutral));
			TraceEntries.Clear();
			TraceEntries.Add(new TraceEntryViewModel("error", exception.Message, DateTime.Now.ToString("T"), _dangerColor));
			OnPropertyChanged(nameof(CanCopyTrace));
			OnPropertyChanged(nameof(ResponseTimeStatus));
			OnPropertyChanged(nameof(ResponseSizeStatus));
			FocusRightPaneTab("debug");
			RevealInspectorOnCompactLayout();
		}
		finally
		{
			IsSending = false;
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
		OnPropertyChanged(nameof(ShowCompactActionBar));
		OnPropertyChanged(nameof(ShowDesktopStatusBar));
		OnPropertyChanged(nameof(ShowCompactStatusBar));
		OnPropertyChanged(nameof(ShowInlineLanguageHelpDrawer));
		OnPropertyChanged(nameof(ShowCompactLanguageHelpDrawer));
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
		OnPropertyChanged(nameof(IsInspectorStashVisible));
		OnPropertyChanged(nameof(IsInspectorHeadersVisible));
		OnPropertyChanged(nameof(IsInspectorTraceVisible));
		OnPropertyChanged(nameof(IsInspectorRawVisible));
		OnPropertyChanged(nameof(IsInspectorDebugVisible));
		OnPropertyChanged(nameof(RightSurfaceStatus));
	}

	public void ToggleResponsePrettyPrint()
	{
		IsResponsePrettyPrintEnabled = !IsResponsePrettyPrintEnabled;
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
			_ = ReloadHistoryAsync();
		}
	}

	public void AddWorkspace()
	{
		CaptureActiveRequestIntoWorkspaceState();
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
			_ = ReloadHistoryAsync();
		}
	}

	public void RenameSelectedWorkspace(string? nextName)
	{
		RequestWorkbenchWorkspaceState? workspace = GetSelectedWorkspaceState();
		if (workspace is null)
		{
			return;
		}

		string normalizedName = string.IsNullOrWhiteSpace(nextName)
			? workspace.Name
			: nextName.Trim();
		if (string.Equals(normalizedName, workspace.Name, StringComparison.Ordinal))
		{
			SelectedWorkspace = workspace.Name;
			return;
		}

		CaptureActiveRequestIntoWorkspaceState();
		RequestWorkbenchWorkspaceState currentWorkspace = GetSelectedWorkspaceState() ?? workspace;
		RequestWorkbenchWorkspaceState renamedWorkspace = RenameWorkspace(currentWorkspace, normalizedName);
		_workspaceStates[renamedWorkspace.Id] = renamedWorkspace;
		ApplyWorkspaceSelection(renamedWorkspace.Id);
		if (_isInitialized)
		{
			_ = PersistWorkbenchStateInBackground();
		}
	}

	public void MoveSelectedWorkspaceLeft()
	{
		MoveSelectedWorkspace(-1);
	}

	public void MoveSelectedWorkspaceRight()
	{
		MoveSelectedWorkspace(1);
	}

	public void AddRequest()
	{
		CaptureActiveRequestIntoWorkspaceState();

		RequestWorkbenchWorkspaceState? workspace = GetSelectedWorkspaceState();
		if (workspace is null)
		{
			return;
		}

		RequestWorkbenchDocumentState request = RequestWorkbenchDocumentFactory.CreateNewRequest(workspace, location => BuildNewRequestTarget(workspace, location));
		UpdateSelectedWorkspaceState(
			currentWorkspace => currentWorkspace with
			{
				SelectedEnvironment = SelectedEnvironment,
				SelectedDocumentLocation = request.Location,
				Documents =
				[
					.. currentWorkspace.Documents,
					request
				]
			});

		ApplyWorkspaceSelection(workspace.Id);
		SelectCenterTab(CenterTabs.FirstOrDefault(static tab => string.Equals(tab.Key, "request", StringComparison.Ordinal)));
		if (_isInitialized)
		{
			_ = PersistWorkbenchStateInBackground();
		}
	}

	public void DeleteSelectedWorkspace()
	{
		RequestWorkbenchWorkspaceState? workspace = GetSelectedWorkspaceState();
		if (workspace is null)
		{
			return;
		}

		int currentIndex = GetSelectedWorkspaceIndex();
		_workspaceStates.Remove(workspace.Id);

		WorkspaceItemViewModel? workspaceItem = Workspaces.FirstOrDefault(item => item.Id == workspace.Id);
		if (workspaceItem is not null)
		{
			Workspaces.Remove(workspaceItem);
		}

		if (Workspaces.Count == 0)
		{
			RequestWorkbenchWorkspaceState replacementWorkspace = BuildUserWorkspace(BuildNextWorkspaceName());
			_workspaceStates[replacementWorkspace.Id] = replacementWorkspace;
			Workspaces.Add(
				new WorkspaceItemViewModel(
					replacementWorkspace.Id,
					replacementWorkspace.Name,
					replacementWorkspace.Documents.Count == 1 ? "1 request" : $"{replacementWorkspace.Documents.Count} requests",
					false));
			currentIndex = 0;
		}

		int nextIndex = Math.Clamp(currentIndex, 0, Workspaces.Count - 1);
		ApplyWorkspaceSelection(Workspaces[nextIndex].Id);
		if (_isInitialized)
		{
			_ = PersistWorkbenchStateInBackground();
			_ = ReloadHistoryAsync();
		}
	}

	public void DeleteSelectedRequest()
	{
		RequestWorkbenchWorkspaceState? workspace = GetSelectedWorkspaceState();
		if (workspace is null)
		{
			return;
		}

		int currentIndex = GetSelectedRequestIndex();
		if (currentIndex < 0 || currentIndex >= workspace.Documents.Count)
		{
			return;
		}

		List<RequestWorkbenchDocumentState> remainingDocuments = [.. workspace.Documents];
		remainingDocuments.RemoveAt(currentIndex);

		if (remainingDocuments.Count == 0)
		{
			RequestWorkbenchWorkspaceState emptyWorkspace = workspace with
			{
				Documents = []
			};
			RequestWorkbenchDocumentState replacementDocument = RequestWorkbenchDocumentFactory.CreateNewRequest(
				emptyWorkspace,
				location => BuildNewRequestTarget(workspace, location));
			remainingDocuments.Add(replacementDocument);
			currentIndex = 0;
		}

		int nextIndex = Math.Clamp(currentIndex, 0, remainingDocuments.Count - 1);
		string nextLocation = remainingDocuments[nextIndex].Location;
		UpdateSelectedWorkspaceState(
			currentWorkspace => currentWorkspace with
			{
				SelectedEnvironment = SelectedEnvironment,
				SelectedDocumentLocation = nextLocation,
				Documents = remainingDocuments
			});

		ApplyWorkspaceSelection(workspace.Id);
		SelectCenterTab(CenterTabs.FirstOrDefault(static tab => string.Equals(tab.Key, "request", StringComparison.Ordinal)));
		if (_isInitialized)
		{
			_ = PersistWorkbenchStateInBackground();
		}
	}

	public void MoveSelectedRequestUp()
	{
		MoveSelectedRequest(-1);
	}

	public void MoveSelectedRequestDown()
	{
		MoveSelectedRequest(1);
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
		CloseExplorerOverlayOnCompactLayout();
	}

	public void SelectHistoryEntry(HistoryEntryViewModel? entry)
	{
		if (entry is null)
		{
			return;
		}

		foreach (HistoryEntryViewModel item in HistoryItems)
		{
			item.IsSelected = ReferenceEquals(item, entry);
		}

		ApplyHistoryRunToInspector(entry.Run, entry.Method);
		RevealInspectorOnCompactLayout();
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
			CloseExplorerOverlayOnCompactLayout();
			return;
		}

		string method = item.Method ?? SelectedMethod;
		ApplyRequestSelection(item.Title, method, item.Detail, item.Context);
		SelectDocumentByLocation(item.Context);
		CloseExplorerOverlayOnCompactLayout();
	}

	public async Task CopyActiveEditorAsync()
	{
		string content = ActiveEditorText ?? string.Empty;
		if (string.IsNullOrWhiteSpace(content))
		{
			ExecutionStatus = "Nothing to copy from the active editor.";
			return;
		}

		await Clipboard.Default.SetTextAsync(content);
		ExecutionStatus = "Copied current editor text.";
	}

	public void ToggleLanguageHelp()
	{
		if (!IsLanguageHelpAvailable)
		{
			return;
		}

		IsLanguageHelpOpen = !IsLanguageHelpOpen;
	}

	public void SelectLanguageHelpEntry(LanguageHelpEntryViewModel? entry)
	{
		ApplySelectedLanguageHelpEntry(entry);
	}

	public async Task CopySelectedLanguageHelpExampleAsync()
	{
		if (string.IsNullOrWhiteSpace(SelectedLanguageHelpExample))
		{
			ExecutionStatus = "No ForRest example is selected.";
			return;
		}

		await Clipboard.Default.SetTextAsync(SelectedLanguageHelpExample);
		ExecutionStatus = $"Copied ForRest example: {SelectedLanguageHelpTitle}.";
	}

	public async Task CopyResponseBodyAsync()
	{
		if (!CanCopyResponseBody)
		{
			ExecutionStatus = "No response body available to copy.";
			return;
		}

		await Clipboard.Default.SetTextAsync(ResponseBodyText);
		ExecutionStatus = "Copied response body.";
	}

	public async Task CopyRawResponseAsync()
	{
		if (!CanCopyRawResponse)
		{
			ExecutionStatus = "No raw exchange available to copy.";
			return;
		}

		await Clipboard.Default.SetTextAsync(ResponseRawText);
		ExecutionStatus = "Copied raw exchange.";
	}

	public async Task CopyHeadersAsync()
	{
		if (!CanCopyHeaders)
		{
			ExecutionStatus = "No response headers available to copy.";
			return;
		}

		string text = ResponsePaneCopyFormatter.BuildHeadersText(ResponseHeaderRows);
		await Clipboard.Default.SetTextAsync(text);
		ExecutionStatus = "Copied response headers.";
	}

	public async Task CopyTraceAsync()
	{
		if (!CanCopyTrace)
		{
			ExecutionStatus = "No trace entries available to copy.";
			return;
		}

		string text = ResponsePaneCopyFormatter.BuildTraceText(TraceEntries);
		await Clipboard.Default.SetTextAsync(text);
		ExecutionStatus = "Copied execution trace.";
	}

	public async Task CopyDebugOutputAsync()
	{
		if (!CanCopyDebugOutput)
		{
			ExecutionStatus = "No debug output available to copy.";
			return;
		}

		await Clipboard.Default.SetTextAsync(DebugOutputText);
		ExecutionStatus = "Copied debug output.";
	}

	public async Task CopyStashAsync()
	{
		if (!CanCopyStash)
		{
			ExecutionStatus = "No stash data available to copy.";
			return;
		}

		string text = ResponsePaneCopyFormatter.BuildStashText(StashColumns, StashRows);
		await Clipboard.Default.SetTextAsync(text);
		ExecutionStatus = "Copied stash table.";
	}

	public async Task ExportStashCsvAsync()
	{
		if (!HasStashData)
		{
			ExecutionStatus = "No stash data available to export.";
			return;
		}

		string csv = BuildStashCsv();
		string fileName = $"forrest-stash-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
		string filePath = Path.Combine(FileSystem.Current.CacheDirectory, fileName);
		await File.WriteAllTextAsync(filePath, csv);
		await Share.Default.RequestAsync(
			new ShareFileRequest
			{
				Title = "Export stash CSV",
				File = new ShareFile(filePath),
			});
		ExecutionStatus = $"Shared stash CSV: {fileName}";
	}

	private void ApplyStashTable(StashTable stash)
	{
		List<string> orderedColumns = [];
		HashSet<string> seenColumns = new(StringComparer.OrdinalIgnoreCase);

		foreach (string column in stash.Columns)
		{
			if (string.IsNullOrWhiteSpace(column) || !seenColumns.Add(column))
			{
				continue;
			}

			orderedColumns.Add(column);
		}

		foreach (StashRow row in stash.Rows)
		{
			foreach (string column in row.Values.Keys)
			{
				if (string.IsNullOrWhiteSpace(column) || !seenColumns.Add(column))
				{
					continue;
				}

				orderedColumns.Add(column);
			}
		}

		StashColumns.Clear();
		foreach (string column in orderedColumns)
		{
			StashColumns.Add(new StashColumnViewModel(column));
		}

		StashRows.Clear();
		foreach (StashRow row in stash.Rows)
		{
			StashRows.Add(
				new StashRowViewModel(
					orderedColumns.Select(
						column => new StashCellViewModel(
							row.Values.TryGetValue(column, out string? value)
								? value
								: string.Empty))));
		}

		NotifyStashStateChanged();
	}

	private void ClearStashTable()
	{
		StashColumns.Clear();
		StashRows.Clear();
		NotifyStashStateChanged();
	}

	private string BuildStashCsv()
	{
		List<string> columns = StashColumns.Select(static column => column.Title).ToList();
		if (columns.Count == 0)
		{
			return string.Empty;
		}

		StringBuilder builder = new();
		builder.AppendLine(string.Join(",", columns.Select(EscapeCsv)));
		foreach (StashRowViewModel row in StashRows)
		{
			builder.AppendLine(string.Join(",", row.Cells.Select(static cell => EscapeCsv(cell.Value))));
		}

		return builder.ToString();
	}

	private void NotifyStashStateChanged()
	{
		OnPropertyChanged(nameof(HasStashData));
		OnPropertyChanged(nameof(ShowStashEmptyState));
		OnPropertyChanged(nameof(CanCopyStash));
		OnPropertyChanged(nameof(CanExportStashCsv));
	}

	private static string EscapeCsv(string value)
	{
		string normalized = value ?? string.Empty;
		if (normalized.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
		{
			return normalized;
		}

		return $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
	}

	public async Task CopyResponseVariableAsync(int lineNumber, int column)
	{
		string rootExpression = ResolveResponseVariableRoot();
		if (!ResponseVariableExpressionService.TryBuildExpression(ResponseBodyText, lineNumber, column, rootExpression, out string expression))
		{
			ExecutionStatus = "No response property was detected at that location.";
			return;
		}

		await Clipboard.Default.SetTextAsync(expression);
		ExecutionStatus = $"Copied response variable: {expression}";
	}

	public void UpdateActiveEditorCursor(int lineNumber, int column)
	{
		_activeEditorLineNumber = Math.Max(1, lineNumber);
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
			UpdateRequestMetadataFromSource();
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
		foreach (NavigationSectionViewModel section in BuildExplorerSections(workspace))
		{
			ExplorerSections.Add(section);
		}

		ExplorerSections.Add(
			new NavigationSectionViewModel(
				"Settings",
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
				],
				"Workspace configuration",
				supportsRequestActions: false));
		OnPropertyChanged(nameof(CanMoveWorkspaceLeft));
		OnPropertyChanged(nameof(CanMoveWorkspaceRight));
		OnPropertyChanged(nameof(CanMoveRequestUp));
		OnPropertyChanged(nameof(CanMoveRequestDown));
		OnPropertyChanged(nameof(CanDeleteWorkspace));
		OnPropertyChanged(nameof(CanDeleteRequest));
	}

	private IReadOnlyList<NavigationSectionViewModel> BuildExplorerSections(RequestWorkbenchWorkspaceState workspace)
	{
		List<NavigationSectionViewModel> sections = [];
		bool supportsRequestActionsAssigned = false;

		AddExplorerSection(
			sections,
			"Requests",
			"Runnable request programs",
			workspace.Documents.Where(document => IsExplorerLocationInBucket(document.Location, "requests")),
			ref supportsRequestActionsAssigned);
		AddExplorerSection(
			sections,
			"Scratch",
			"Ad hoc probes and experiments",
			workspace.Documents.Where(document => IsExplorerLocationInBucket(document.Location, "scratch")),
			ref supportsRequestActionsAssigned);
		AddExplorerSection(
			sections,
			"Scripts",
			"Shared helpers and reusable flows",
			workspace.Documents.Where(document => IsExplorerLocationInBucket(document.Location, "scripts")),
			ref supportsRequestActionsAssigned);
		AddExplorerSection(
			sections,
			"Files",
			"Other workspace documents",
			workspace.Documents.Where(document => IsExplorerLocationInBucket(document.Location, "files")),
			ref supportsRequestActionsAssigned);

		if (!supportsRequestActionsAssigned)
		{
			sections.Add(new NavigationSectionViewModel("Requests", [], "No request files yet", supportsRequestActions: true));
		}

		return sections;
	}

	private void AddExplorerSection(
		ICollection<NavigationSectionViewModel> sections,
		string title,
		string description,
		IEnumerable<RequestWorkbenchDocumentState> documents,
		ref bool supportsRequestActionsAssigned)
	{
		List<NavigationItemViewModel> items =
		[
			.. documents.Select(
				document => new NavigationItemViewModel(
					document.Method.ToUpperInvariant(),
					document.Title,
					document.Summary,
					document.Location,
					ResolveMethodAccent(document.Method),
					document.Method))
		];

		if (items.Count == 0)
		{
			return;
		}

		string subtitle = $"{items.Count} {(items.Count == 1 ? "file" : "files")}  {description}";
		sections.Add(new NavigationSectionViewModel(title, items, subtitle, supportsRequestActions: !supportsRequestActionsAssigned));
		supportsRequestActionsAssigned = true;
	}

	private static bool IsExplorerLocationInBucket(string location, string bucket)
	{
		string normalized = NormalizeExplorerLocation(location);
		return bucket switch
		{
			"requests" => normalized.StartsWith("requests/", StringComparison.OrdinalIgnoreCase),
			"scratch" => normalized.StartsWith("scratch/", StringComparison.OrdinalIgnoreCase),
			"scripts" => normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase),
			"files" => !normalized.StartsWith("requests/", StringComparison.OrdinalIgnoreCase)
			           && !normalized.StartsWith("scratch/", StringComparison.OrdinalIgnoreCase)
			           && !normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase),
			_ => false
		};
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

	private static RequestWorkbenchWorkspaceState RenameWorkspace(RequestWorkbenchWorkspaceState workspace, string nextName)
	{
		string oldSlug = BuildSlug(workspace.Name, "workspace");
		string newSlug = BuildSlug(nextName, "workspace");
		List<RequestWorkbenchDocumentState> renamedDocuments =
		[
			.. workspace.Documents.Select(document => document with
			{
				Location = RebaseWorkspaceLocation(document.Location, oldSlug, newSlug)
			})
		];

		return workspace with
		{
			Name = nextName,
			SelectedDocumentLocation = RebaseWorkspaceLocation(workspace.SelectedDocumentLocation, oldSlug, newSlug),
			Documents = renamedDocuments
		};
	}

	private static string RebaseWorkspaceLocation(string location, string oldSlug, string newSlug)
	{
		string[] segments = NormalizeExplorerLocation(location)
			.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (segments.Length < 2 || !string.Equals(segments[1], oldSlug, StringComparison.OrdinalIgnoreCase))
		{
			return location;
		}

		segments[1] = newSlug;
		return "/" + string.Join('/', segments);
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
		string workspaceSlug = BuildSlug(workspaceName, "workspace");
		return BuildWorkspace(
			Guid.NewGuid(),
			workspaceName,
			[
				BuildDefaultDocumentState(
					"Get Headers",
					"GET",
					"Inspect request headers round-trip",
					$"/requests/{workspaceSlug}/get-headers",
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
					$"/requests/{workspaceSlug}/post-echo",
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
					"Fetch a quick generated identifier",
					$"/requests/{workspaceSlug}/get-uuid",
					"https://httpbin.org/uuid",
					tests:
					[
						"status == 200 \"returns 200\"",
						"body contains \"uuid\" \"uuid field exists\""
					])
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
		if (!(e.IsPreview && IsActiveSettingsEditor))
		{
			UpdateSettingsTextFromDisk(_currentThemeName);
		}

		RefreshActivationStatus();
	}

	private void RefreshActivationStatus()
	{
		try
		{
			ActivationSnapshot snapshot = _appActivationService.EvaluateNow();
			ActivationStatus = snapshot.StatusText;
			ActivationDetail = snapshot.DetailText;
			_canExecuteRequests = snapshot.CanExecuteRequests;
		}
		catch (Exception exception)
		{
			ActivationStatus = "Activation unavailable";
			ActivationDetail = exception.Message;
			_canExecuteRequests = true;
			AppLaunchGuard.RecordException("Activation status refresh failed.", exception);
		}

		OnPropertyChanged(nameof(CanSend));
	}

	private void ApplyActivationBlock()
	{
		ResponseState = "Blocked";
		ExecutionStatus = ActivationStatus;
		DebugOutputText = string.Join(
			Environment.NewLine,
			[
				"Execution was blocked by activation policy.",
				$"Status: {ActivationStatus}",
				$"Detail: {ActivationDetail}"
			]);
		_responseTimeStatus = "--";
		_responseSizeStatus = "--";
		_latestResponseSnapshot = null;
		ResponseBodyText = string.Empty;
		ResponseRawText = string.Empty;
		ResponseHeaderRows.Clear();
		OnPropertyChanged(nameof(CanCopyHeaders));
		ClearStashTable();
		OutputMetrics.Clear();
		OutputMetrics.Add(new OutputMetricViewModel("Status", "Blocked", _dangerColor));
		OutputMetrics.Add(new OutputMetricViewModel("Time", "--", _methodNeutral));
		OutputMetrics.Add(new OutputMetricViewModel("Size", "--", _methodNeutral));
		OutputMetrics.Add(new OutputMetricViewModel("Type", "n/a", _methodNeutral));
		TraceEntries.Clear();
		TraceEntries.Add(new TraceEntryViewModel("license", ActivationStatus, DateTime.Now.ToString("T"), _dangerColor));
		OnPropertyChanged(nameof(CanCopyTrace));
		OnPropertyChanged(nameof(ResponseTimeStatus));
		OnPropertyChanged(nameof(ResponseSizeStatus));
		FocusRightPaneTab("debug");
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

	private int GetSelectedRequestIndex()
	{
		RequestWorkbenchWorkspaceState? workspace = GetSelectedWorkspaceState();
		if (workspace is null)
		{
			return -1;
		}

		return workspace.Documents.FindIndex(
			document => string.Equals(document.Location, RequestLocation, StringComparison.OrdinalIgnoreCase));
	}

	private int GetSelectedWorkspaceIndex()
	{
		for (int index = 0; index < Workspaces.Count; index++)
		{
			if (Workspaces[index].Id == _selectedWorkspaceId)
			{
				return index;
			}
		}

		return -1;
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
		_requestEditorDiagnosticsJson = string.IsNullOrWhiteSpace(state.DiagnosticsJson) ? "[]" : state.DiagnosticsJson;

		_suppressRequestAutosave = true;
		_suppressDocumentSynchronization = true;
		try
		{
			RequestName = state.Title;
			SelectedMethod = state.Method;
			RequestSummary = state.Summary;
			RequestLocation = state.Location;
			RequestEditorText = NormalizeLineEndings(RequestWorkbenchDocumentNormalizer.NormalizeRequestDocumentSource(state.RequestSource, state.PreRequestScript, state.Title));
			ScriptEditorText = string.Empty;
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
		OnPropertyChanged(nameof(ShowEditorDebugStrip));
		OnPropertyChanged(nameof(IsLanguageHelpAvailable));
		OnPropertyChanged(nameof(ShowLanguageHelpToggle));
		OnPropertyChanged(nameof(ShowLanguageHelpDrawer));
		OnPropertyChanged(nameof(ShowInlineLanguageHelpDrawer));
		OnPropertyChanged(nameof(ShowCompactLanguageHelpDrawer));
		OnPropertyChanged(nameof(CanMoveRequestUp));
		OnPropertyChanged(nameof(CanMoveRequestDown));
		OnPropertyChanged(nameof(CanDeleteRequest));
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
		ActiveEditorDiagnosticsJson = "[]";
		SetActiveEditorTextInternal(_themeConfigText);
		ForceActiveEditorRefresh();
		OnPropertyChanged(nameof(ShowEditorDebugStrip));
		OnPropertyChanged(nameof(IsLanguageHelpAvailable));
		OnPropertyChanged(nameof(ShowLanguageHelpToggle));
		OnPropertyChanged(nameof(ShowLanguageHelpDrawer));
		OnPropertyChanged(nameof(ShowInlineLanguageHelpDrawer));
		OnPropertyChanged(nameof(ShowCompactLanguageHelpDrawer));
		OnPropertyChanged(nameof(CanSend));
		OnPropertyChanged(nameof(CanMoveRequestUp));
		OnPropertyChanged(nameof(CanMoveRequestDown));
	}

	private void ActivateCurrentCenterTabEditor()
	{
		if (!IsActiveRequestEditor)
		{
			return;
		}

		ActiveDocumentLabel = RequestDocumentLabel;
		ActiveDocumentKindLabel = SelectedMethod;
		ActiveDocumentKindColor = SelectedMethodColor;
		ActiveEditorLanguage = "forrest";
		ActiveEditorEditableRangesJson = "[]";
		ActiveEditorDiagnosticsJson = _requestEditorDiagnosticsJson;
		SetActiveEditorTextInternal(RequestEditorText);
		ForceActiveEditorRefresh();
		OnPropertyChanged(nameof(ShowEditorDebugStrip));
		OnPropertyChanged(nameof(IsLanguageHelpAvailable));
		OnPropertyChanged(nameof(ShowLanguageHelpToggle));
		OnPropertyChanged(nameof(ShowLanguageHelpDrawer));
		OnPropertyChanged(nameof(CanSend));
		OnPropertyChanged(nameof(CanDeleteRequest));
	}

	private string GetSelectedCenterTabKey()
	{
		return "request";
	}

	private void ApplyRequestEditorChange(string value)
	{
		_requestEditorText = NormalizeLineEndings(value);
		SyncSupportEditorsFromRequestSource();

		RequestMetadataRefresh();
		MarkCurrentDocumentDirty();
		if (!_suppressRequestAutosave)
		{
			ScheduleRequestAutosave();
		}
	}

	private void RequestMetadataRefresh()
	{
		if (!ShouldDebounceRequestMetadataRefresh())
		{
			CancelPendingRequestMetadataRefresh();
			UpdateRequestMetadataFromSource();
			return;
		}

		int refreshVersion = Interlocked.Increment(ref _requestMetadataRefreshVersion);
		string source = _requestEditorText;
		Guid workspaceId = GetSelectedWorkspaceState()?.Id ?? HttpBinWorkspaceId;
		string requestName = RequestName;
		string workspaceName = SelectedWorkspace;
		string environmentName = SelectedEnvironment;
		string fallbackTarget = BuildDefaultRequestUrl(RequestLocation);

		CancellationTokenSource refreshSource = new();
		CancellationTokenSource? previousSource = Interlocked.Exchange(ref _requestMetadataRefreshSource, refreshSource);
		previousSource?.Cancel();
		previousSource?.Dispose();

		_ = Task.Run(
			async () =>
			{
				try
				{
					await Task.Delay(450, refreshSource.Token);
					ForRestScriptCompilationResult compilation = _scriptExecutionService.Compile(
						source,
						workspaceId,
						requestName);
					await MainThread.InvokeOnMainThreadAsync(
						() =>
						{
							if (refreshSource.IsCancellationRequested ||
							    refreshVersion != Volatile.Read(ref _requestMetadataRefreshVersion) ||
							    !string.Equals(_requestEditorText, source, StringComparison.Ordinal))
							{
								return;
							}

							ApplyRequestMetadataCompilation(
								compilation,
								source,
								workspaceName,
								environmentName,
								requestName,
								fallbackTarget);
						});
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception exception)
				{
					await MainThread.InvokeOnMainThreadAsync(
						() =>
						{
							if (refreshSource.IsCancellationRequested ||
							    refreshVersion != Volatile.Read(ref _requestMetadataRefreshVersion))
							{
								return;
							}

							HandleRequestMetadataFailure(exception);
						});
				}
				finally
				{
					if (ReferenceEquals(Volatile.Read(ref _requestMetadataRefreshSource), refreshSource))
					{
						Interlocked.CompareExchange(ref _requestMetadataRefreshSource, null, refreshSource);
					}

					refreshSource.Dispose();
				}
			});
	}

	private bool ShouldDebounceRequestMetadataRefresh()
	{
		return OperatingSystem.IsAndroid() &&
		       IsActiveRequestEditor;
	}

	private void CancelPendingRequestMetadataRefresh()
	{
		CancellationTokenSource? pendingSource = Interlocked.Exchange(ref _requestMetadataRefreshSource, null);
		pendingSource?.Cancel();
		pendingSource?.Dispose();
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
		catch (Exception exception)
		{
			_headersEditorText = string.Empty;
			_bodyEditorText = string.Empty;
			_testsEditorText = string.Empty;
			_variablesEditorText = string.Empty;
			_requestBodyMode = RequestBodyMode.Json;
			AppLaunchGuard.RecordException("Failed to synchronize support editors from the request source.", exception);
		}
		finally
		{
			_suppressDocumentSynchronization = false;
		}
	}

	private void UpdateRequestMetadataFromSource()
	{
		CancelPendingRequestMetadataRefresh();
		Interlocked.Increment(ref _requestMetadataRefreshVersion);

		try
		{
			ForRestScriptCompilationResult compilation = _scriptExecutionService.Compile(
				_requestEditorText,
				GetSelectedWorkspaceState()?.Id ?? HttpBinWorkspaceId,
				RequestName);
			ApplyRequestMetadataCompilation(
				compilation,
				_requestEditorText,
				SelectedWorkspace,
				SelectedEnvironment,
				RequestName,
				BuildDefaultRequestUrl(RequestLocation));
		}
		catch (Exception exception)
		{
			HandleRequestMetadataFailure(exception);
		}
	}

	private void ApplyRequestMetadataCompilation(
		ForRestScriptCompilationResult compilation,
		string source,
		string workspaceName,
		string environmentName,
		string fallbackRequestName,
		string fallbackTarget)
	{
		ApplyEditorDebugSnapshot(
			ForRestEditorDebugSnapshotFactory.Create(
				source,
				compilation,
				workspaceName,
				environmentName,
				fallbackRequestName,
				fallbackTarget));

		if (!compilation.Succeeded || compilation.Payload is null)
		{
			ExecutionStatus = compilation.Diagnostics.Count == 0
				? "Editing request document"
				: string.Join("  ", compilation.Diagnostics.Take(3).Select(static diagnostic => $"L{diagnostic.Line}: {diagnostic.Message}"));
			RequestTarget = fallbackTarget;
			return;
		}

		RequestName = compilation.Payload.Request.Name;
		SelectedMethod = compilation.Payload.Request.Method.ToString().ToUpperInvariant();
		RequestTarget = compilation.Payload.Request.UrlTemplate;
		RequestSummary = string.IsNullOrWhiteSpace(RequestSummary) ? $"{SelectedMethod} request" : RequestSummary;
		ExecutionStatus = "Request document ready";
		UpdateCurrentDocumentMetadata();
	}

	private void HandleRequestMetadataFailure(Exception exception)
	{
		RequestTarget = BuildDefaultRequestUrl(RequestLocation);
		ExecutionStatus = "Request document unavailable.";
		EditorDebugStateText = "Metadata recovery";
		EditorDebugSummaryText = "Request metadata unavailable";
		EditorDebugDetailText = "ForRest recovered from a request metadata failure.";
		EditorDebugAccentColor = _dangerColor;
		DebugOutputText = exception.ToString();
		AppLaunchGuard.RecordException("Request metadata update failed.", exception);
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
			RequestSource = NormalizeCurrentRequestEditorSource(applyToEditor: false),
			PreRequestScript = string.Empty,
			DiagnosticsJson = _requestEditorDiagnosticsJson
		};
	}

	private string NormalizeCurrentRequestEditorSource(bool applyToEditor)
	{
		string normalized = NormalizeLineEndings(
			RequestWorkbenchDocumentNormalizer.NormalizeRequestDocumentSource(
				_requestEditorText,
				preRequestScript: string.Empty,
				string.IsNullOrWhiteSpace(RequestName) ? "Untitled Request" : RequestName));
		if (!applyToEditor || string.Equals(_requestEditorText, normalized, StringComparison.Ordinal))
		{
			return normalized;
		}

		_requestEditorText = normalized;
		OnPropertyChanged(nameof(RequestEditorText));
		if (IsActiveRequestEditor && !string.Equals(_activeEditorText, normalized, StringComparison.Ordinal))
		{
			SetActiveEditorTextInternal(normalized);
		}

		SyncSupportEditorsFromRequestSource();
		UpdateRequestMetadataFromSource();
		MarkCurrentDocumentDirty();
		return normalized;
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
		_latestResponseSnapshot = null;
		ResponseBodyText = string.Empty;
		ResponseRawText = string.Empty;
		DebugOutputText = string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => $"Line {diagnostic.Line}, Col {diagnostic.Column}: {diagnostic.Message}"));
		_responseTimeStatus = "--";
		_responseSizeStatus = "--";
		ExecutionStatus = diagnostics.Count == 0
			? "Request document failed to compile"
			: diagnostics[0].Message;
		ResponseHeaderRows.Clear();
		OnPropertyChanged(nameof(CanCopyHeaders));
		TraceEntries.Clear();
		TraceEntries.Add(new TraceEntryViewModel("compile", ExecutionStatus, DateTime.Now.ToString("T"), _dangerColor));
		OnPropertyChanged(nameof(CanCopyTrace));
		ClearStashTable();
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

	private void MoveSelectedRequest(int offset)
	{
		if (offset == 0)
		{
			return;
		}

		CaptureActiveRequestIntoWorkspaceState();
		RequestWorkbenchWorkspaceState? workspace = GetSelectedWorkspaceState();
		if (workspace is null)
		{
			return;
		}

		int currentIndex = workspace.Documents.FindIndex(
			document => string.Equals(document.Location, RequestLocation, StringComparison.OrdinalIgnoreCase));
		if (currentIndex < 0)
		{
			return;
		}

		int nextIndex = currentIndex + offset;
		if (nextIndex < 0 || nextIndex >= workspace.Documents.Count)
		{
			return;
		}

		List<RequestWorkbenchDocumentState> reordered = [.. workspace.Documents];
		(reordered[currentIndex], reordered[nextIndex]) = (reordered[nextIndex], reordered[currentIndex]);
		UpdateSelectedWorkspaceState(
			currentWorkspace => currentWorkspace with
			{
				SelectedEnvironment = SelectedEnvironment,
				SelectedDocumentLocation = RequestLocation,
				Documents = reordered
			});

		ApplyWorkspaceSelection(workspace.Id);
		if (_isInitialized)
		{
			_ = PersistWorkbenchStateInBackground();
		}
	}

	private void MoveSelectedWorkspace(int offset)
	{
		if (offset == 0)
		{
			return;
		}

		CaptureActiveRequestIntoWorkspaceState();
		int currentIndex = GetSelectedWorkspaceIndex();
		if (currentIndex < 0)
		{
			return;
		}

		int nextIndex = currentIndex + offset;
		if (nextIndex < 0 || nextIndex >= Workspaces.Count)
		{
			return;
		}

		Workspaces.Move(currentIndex, nextIndex);
		OnPropertyChanged(nameof(CanMoveWorkspaceLeft));
		OnPropertyChanged(nameof(CanMoveWorkspaceRight));
		if (_isInitialized)
		{
			_ = PersistWorkbenchStateInBackground();
		}
	}

	private void CaptureActiveRequestIntoWorkspaceState()
	{
		if (!IsActiveRequestEditor || string.IsNullOrWhiteSpace(RequestLocation))
		{
			return;
		}

		RequestWorkbenchDocumentState currentRequest = BuildCurrentDocumentState();
		UpdateSelectedWorkspaceState(
			workspace => workspace with
			{
				SelectedEnvironment = SelectedEnvironment,
				Documents = UpsertDocument(workspace.Documents, currentRequest)
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
			Nodes = BuildWorkspaceNodes(workspaceState),
			Environments = BuildEnvironmentDefinition() is { } environment ? [environment] : []
		};
	}

	private List<WorkspaceNodeDefinition> BuildWorkspaceNodes(RequestWorkbenchWorkspaceState workspaceState)
	{
		List<WorkspaceNodeDefinition> nodes = [];
		int sortOrder = 0;

		foreach (RequestWorkbenchDocumentState document in workspaceState.Documents)
		{
			string normalizedSource = NormalizeLineEndings(
				RequestWorkbenchDocumentNormalizer.NormalizeRequestDocumentSource(
					document.RequestSource,
					document.PreRequestScript,
					document.Title));
			ForRestScriptCompilationResult compilation = _scriptExecutionService.Compile(normalizedSource, workspaceState.Id, document.Title);

			nodes.Add(
				new WorkspaceNodeDefinition
				{
					WorkspaceId = workspaceState.Id,
					Kind = WorkspaceNodeKind.Request,
					Name = document.Title,
					Location = document.Location,
					SortOrder = sortOrder++,
					Request = compilation.Payload?.Request,
				});
		}

		return nodes;
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

	private void RefreshLanguageHelpEntries()
	{
		List<LanguageHelpEntryViewModel> matches =
		[
			.. _languageHelpSourceEntries.Where(entry => entry.MatchesSearch(LanguageHelpSearchText))
		];

		LanguageHelpEntries.Clear();
		foreach (LanguageHelpEntryViewModel entry in matches)
		{
			LanguageHelpEntries.Add(entry);
		}

		LanguageHelpEntryViewModel? selectedEntry = matches.FirstOrDefault(entry => string.Equals(entry.Key, _selectedLanguageHelpKey, StringComparison.Ordinal))
			?? matches.FirstOrDefault();
		ApplySelectedLanguageHelpEntry(selectedEntry);
		OnPropertyChanged(nameof(LanguageHelpEmptyStateText));
	}

	private void ApplySelectedLanguageHelpEntry(LanguageHelpEntryViewModel? entry)
	{
		_selectedLanguageHelpKey = entry?.Key ?? string.Empty;
		SelectedLanguageHelpTitle = entry?.Title ?? string.Empty;
		SelectedLanguageHelpCategory = entry?.Category ?? string.Empty;
		SelectedLanguageHelpSummary = entry?.Summary ?? string.Empty;
		SelectedLanguageHelpDocumentation = entry?.Documentation ?? string.Empty;
		SelectedLanguageHelpExample = entry?.Example ?? string.Empty;
		OnPropertyChanged(nameof(HasSelectedLanguageHelpEntry));
		OnPropertyChanged(nameof(ShowLanguageHelpEmptyState));
		OnPropertyChanged(nameof(CanCopyLanguageHelpExample));
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
		try
		{
			return _settingsTomlDocumentService.LoadOrCreate(new ForRestSettings(currentTheme));
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Settings text load failed.", exception);
			return string.Empty;
		}
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
		OnPropertyChanged(nameof(CompactLeftPaneButtonText));
		OnPropertyChanged(nameof(CompactRightPaneButtonText));
	}

	private void CloseExplorerOverlayOnCompactLayout()
	{
		if (!_isCompactLayout || !_isExplorerOverlayOpen)
		{
			return;
		}

		_isExplorerOverlayOpen = false;
		NotifyOverlayChanged();
	}

	private void RevealInspectorOnCompactLayout()
	{
		if (!_isCompactLayout)
		{
			return;
		}

		bool changed = _isExplorerOverlayOpen || !_isInspectorOverlayOpen;
		_isExplorerOverlayOpen = false;
		_isInspectorOverlayOpen = true;
		if (changed)
		{
			NotifyOverlayChanged();
		}
	}

	private void RefreshResponsePresentation()
	{
		ResponseBodyText = ResponsePresentationFormatter.FormatBody(_latestResponseSnapshot?.Body, IsResponsePrettyPrintEnabled);
		ResponseRawText = ResponsePresentationFormatter.NormalizeDisplayText(_latestResponseSnapshot?.RawResponse);
	}

	private void ApplyEditorDebugSnapshot(ForRestEditorDebugSnapshot snapshot)
	{
		_requestEditorDiagnosticsJson = snapshot.DiagnosticsJson;
		if (IsActiveRequestEditor)
		{
			ActiveEditorDiagnosticsJson = snapshot.DiagnosticsJson;
		}

		if (IsActiveRequestEditor && !string.IsNullOrWhiteSpace(RequestLocation))
		{
			RequestWorkbenchDocumentState currentRequest = BuildCurrentDocumentState();
			UpdateSelectedWorkspaceState(
				workspace => workspace with
				{
					Documents = UpsertDocument(workspace.Documents, currentRequest)
				});
		}

		EditorDebugStateText = snapshot.StatusText;
		EditorDebugSummaryText = snapshot.SummaryText;
		EditorDebugDetailText = snapshot.DetailText;
		EditorDebugAccentColor = ResolveEditorDebugAccentColor(snapshot.State);
		DebugOutputText = snapshot.DebugOutputText;
	}

	private async Task ReloadHistoryAsync(Guid? selectedRunId = null)
	{
		try
		{
			List<ExecutionRun> historyRuns = await _executionHistoryRepository.Load(GetSelectedWorkspaceState()?.Id ?? HttpBinWorkspaceId);
			HistoryItems.Clear();
			foreach (ExecutionRun run in historyRuns.Take(8))
			{
				HistoryItems.Add(CreateHistoryEntry(run, selectedRunId));
			}
		}
		catch (Exception exception)
		{
			HistoryItems.Clear();
			ExecutionStatus = $"History unavailable: {exception.Message}";
		}
	}

	private HistoryEntryViewModel CreateHistoryEntry(ExecutionRun run, Guid? selectedRunId)
	{
		string method = ResolveHistoryMethod(run);
		string summary = run.Response is not null
			? $"{run.Response.StatusCode} in {run.Response.DurationMilliseconds} ms"
			: !string.IsNullOrWhiteSpace(run.ErrorMessage)
				? run.ErrorMessage
				: run.State.ToString();

		return new HistoryEntryViewModel(
			run,
			method,
			run.RequestName,
			summary,
			run.StartedUtc.ToLocalTime().ToString("t"),
			ResolveMethodAccent(method),
			selectedRunId.HasValue && run.Id == selectedRunId.Value);
	}

	private static string ResolveHistoryMethod(ExecutionRun run)
	{
		if (string.IsNullOrWhiteSpace(run.RawRequest))
		{
			return "RUN";
		}

		string firstLine = run.RawRequest
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault()
			?? string.Empty;
		string method = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
		return method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "OPTIONS" or "HEAD"
			? method
			: "RUN";
	}

	private void ApplyHistoryRunToInspector(ExecutionRun run, string? methodOverride = null)
	{
		string method = string.IsNullOrWhiteSpace(methodOverride) ? ResolveHistoryMethod(run) : methodOverride;
		Color methodAccent = ResolveMethodAccent(method);
		ResponseSnapshot? response = run.Response;

		ResponseState = response is not null
			? $"{response.StatusCode} {response.ReasonPhrase}".Trim()
			: run.State.ToString();
		ExecutionStatus = $"Loaded history run: {run.RequestName}";
		_latestResponseSnapshot = response;
		RefreshResponsePresentation();
		_responseTimeStatus = response is not null ? $"{response.DurationMilliseconds} ms" : "--";
		_responseSizeStatus = response is not null ? FormatResponseSize(response.SizeBytes) : "--";
		DebugOutputText = BuildDebugOutput(run, SelectedWorkspace, SelectedEnvironment, method);

		ResponseHeaderRows.Clear();
		foreach (KeyValueDefinition header in response?.Headers ?? [])
		{
			ResponseHeaderRows.Add(new NameValueRowViewModel(header.Key, header.Value, "response"));
		}
		OnPropertyChanged(nameof(CanCopyHeaders));
		ApplyStashTable(run.Stash);

		TraceEntries.Clear();
		TraceEntries.Add(new TraceEntryViewModel("history", run.RequestName, run.StartedUtc.ToLocalTime().ToString("T"), _methodNeutral));
		if (response is not null)
		{
			TraceEntries.Add(new TraceEntryViewModel("send", ResponseState, response.ReceivedUtc.ToLocalTime().ToString("T"), methodAccent));
		}

		if (!string.IsNullOrWhiteSpace(run.ErrorMessage))
		{
			TraceEntries.Add(new TraceEntryViewModel("error", run.ErrorMessage, (run.CompletedUtc ?? run.StartedUtc).ToLocalTime().ToString("T"), _dangerColor));
		}

		if (run.Tests.Count > 0)
		{
			string summary = $"{run.Tests.Count(static item => item.State == TestOutcomeState.Passed)}/{run.Tests.Count} tests passed";
			TraceEntries.Add(new TraceEntryViewModel("tests", summary, (run.CompletedUtc ?? run.StartedUtc).ToLocalTime().ToString("T"), run.Tests.All(static item => item.State == TestOutcomeState.Passed) ? _successColor : _dangerColor));
		}
		OnPropertyChanged(nameof(CanCopyTrace));

		OutputMetrics.Clear();
		OutputMetrics.Add(new OutputMetricViewModel("Status", ResponseState, response is not null && response.StatusCode is >= 200 and < 300 ? _successColor : _dangerColor));
		OutputMetrics.Add(new OutputMetricViewModel("Time", _responseTimeStatus, methodAccent));
		OutputMetrics.Add(new OutputMetricViewModel("Size", _responseSizeStatus, _methodNeutral));
		OutputMetrics.Add(new OutputMetricViewModel("Type", response?.ContentType ?? "n/a", methodAccent));
		OnPropertyChanged(nameof(ResponseTimeStatus));
		OnPropertyChanged(nameof(ResponseSizeStatus));

		FocusRightPaneTab(response is not null && string.IsNullOrWhiteSpace(run.ErrorMessage) ? "response" : "debug");
	}

	private string BuildDebugOutput(ForRestScriptExecutionOutcome outcome)
	{
		ExecutionRun? latestRun = outcome.Execution?.Runs.LastOrDefault();
		string target = string.IsNullOrWhiteSpace(latestRun?.TargetUri)
			? outcome.Compilation.Payload?.Request.UrlTemplate ?? RequestTarget
			: latestRun.TargetUri;
		List<string> lines =
		[
			$"Workspace: {SelectedWorkspace}",
			$"Environment: {SelectedEnvironment}",
			$"Request: {SelectedMethod} {RequestName}",
			$"Target: {target}"
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

		if (outcome.Execution.Stash.Columns.Count > 0 || outcome.Execution.Stash.Rows.Count > 0)
		{
			lines.Add($"Stash: {outcome.Execution.Stash.Rows.Count} rows across {outcome.Execution.Stash.Columns.Count} columns");
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

	private string BuildDebugOutput(ExecutionRun run, string workspaceName, string environmentName, string requestMethod)
	{
		List<string> lines =
		[
			$"Workspace: {workspaceName}",
			$"Environment: {environmentName}",
			$"Request: {requestMethod} {run.RequestName}",
			$"Target: {run.TargetUri}"
		];

		lines.Add(string.Empty);
		lines.Add($"Execution state: {run.State}");
		if (run.Response is { } response)
		{
			lines.Add($"Response: {response.StatusCode} {response.ReasonPhrase}".Trim());
			lines.Add($"Duration: {response.DurationMilliseconds} ms");
			lines.Add($"Size: {FormatResponseSize(response.SizeBytes)}");
		}

		if (!string.IsNullOrWhiteSpace(run.ErrorMessage))
		{
			lines.Add($"Error: {run.ErrorMessage}");
		}

		if (run.Stash.Columns.Count > 0 || run.Stash.Rows.Count > 0)
		{
			lines.Add($"Stash: {run.Stash.Rows.Count} rows across {run.Stash.Columns.Count} columns");
		}

		if (run.ConsoleEntries.Count > 0)
		{
			lines.Add(string.Empty);
			lines.Add("Console:");
			lines.AddRange(run.ConsoleEntries.Select(entry => $"  [{entry.Level}] {entry.Message}"));
		}

		if (run.Tests.Count > 0)
		{
			lines.Add(string.Empty);
			lines.Add("Tests:");
			lines.AddRange(run.Tests.Select(entry => $"  [{entry.State}] {entry.Name}"));
		}

		return string.Join(Environment.NewLine, lines);
	}

	private Color ResolveEditorDebugAccentColor(ForRestEditorDebugState state)
	{
		return state switch
		{
			ForRestEditorDebugState.Error => _dangerColor,
			ForRestEditorDebugState.Warning => _warningColor,
			_ => _successColor,
		};
	}

	private void FocusRightPaneTab(string key)
	{
		PaneTabViewModel? tab = RightPaneTabs.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
		if (tab is not null)
		{
			SelectRightPaneTab(tab);
		}
	}

	private string ResolveResponseVariableRoot()
	{
		return IsActiveRequestEditor
			? ResponseVariableRootResolver.Resolve(RequestEditorText, _activeEditorLineNumber)
			: "response";
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
						scriptLines:
						[
							"# Probe a collection response like code, not a form.",
							"request.headers[\"X-Request-Source\"] = \"maui\"",
							"let attempts = [0..1]",
							"let sent = null",
							string.Empty,
							"foreach attempt in attempts {",
							"  sent = request.send()",
							"  if sent.status == 200 and sent.length() > 2 {",
							"    runtime first_user_email = sent[0].email",
							"    foreach index in [0..2] {",
							"      log sent[index].username",
							"    }",
							"    break",
							"  }",
							"",
							"  warn $\"Attempt {attempt} returned {sent.status}.\"",
							"}",
							"",
							"if sent == null or sent.length() == 0 {",
							"  error \"The users feed did not return a usable collection.\"",
							"} else {",
							"  log $\"Loaded {sent.length()} users.\"",
							"  }",
						],
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
			RequestSource = BuildRequestEditorText(title, method, target, bodyContent, tests, variableLines, scriptLines),
			PreRequestScript = string.Empty
		};
	}

	private static string BuildDefaultRequestUrl(string location)
	{
		return $"https://httpbin.org/anything?source={Uri.EscapeDataString(location)}";
	}

	private static string BuildNewRequestTarget(RequestWorkbenchWorkspaceState workspace, string location)
	{
		if (workspace.Id == JsonPlaceholderWorkspaceId
		    || workspace.Documents.Any(static document => document.Location.Contains("/requests/jsonplaceholder/", StringComparison.OrdinalIgnoreCase))
		    || workspace.Name.Contains("json placeholder", StringComparison.OrdinalIgnoreCase))
		{
			return "https://jsonplaceholder.typicode.com/posts/1";
		}

		return BuildDefaultRequestUrl(location);
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
		IReadOnlyList<string>? variableLines = null,
		IReadOnlyList<string>? scriptLines = null)
	{
		string escapedTitle = EscapeForForRestString(title);
		string escapedTarget = EscapeForForRestString(target);
		List<string> lines =
		[
			$"name \"{escapedTitle}\"",
			$"method {method}",
			$"url \"{escapedTarget}\"",
			"timeout 15000",
			"max_send_iterations 3",
			"redirects true",
			"ssl true",
			"history true"
		];

		if (!string.IsNullOrWhiteSpace(bodyContent))
		{
			lines.Add("content_type \"application/json\"");
		}

		lines.Add(string.Empty);
		foreach (string variableLine in variableLines ?? ["runtime trace_id = guid()"])
		{
			lines.Add(variableLine);
		}

		lines.Add(string.Empty);
		lines.Add("header \"Accept\" = \"application/json\"");
		lines.Add("header \"X-Workspace\" = \"{{workspace_name}}\"");
		lines.Add("header \"X-Environment\" = \"{{environment_name}}\"");
		lines.Add("header \"X-Correlation-Id\" = \"{{trace_id}}\"");

		if (!string.IsNullOrWhiteSpace(bodyContent))
		{
			lines.Add(string.Empty);
			lines.Add("body json \"\"\"");
			lines.AddRange(NormalizeLineEndings(bodyContent).Split('\n'));
			lines.Add("\"\"\"");
		}

		lines.Add(string.Empty);
		foreach (string scriptLine in BuildScriptEditorLines(scriptLines))
		{
			lines.Add(scriptLine);
		}

		lines.Add(string.Empty);
		foreach (string testLine in tests ?? ["status == 200 \"returns 200\"", "header \"Content-Type\" contains \"json\" \"json response\""])
		{
			lines.Add($"expect {testLine}");
		}

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
				"  \"createdBy\": \"for-rest\",",
				"  \"traceId\": \"{{trace_id}}\"",
				"}"
			]);
	}

	private static IReadOnlyList<string> BuildScriptEditorLines(IReadOnlyList<string>? scriptLines = null)
	{
		return scriptLines
			??
			[
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
				"",
				"  warn $\"Attempt {attempt} returned {sent.status}.\"",
				"}",
				string.Empty,
				"if sent == null or sent.status != 200 {",
				"  error \"The request never returned HTTP 200.\"",
				"} else {",
				"  runtime last_status = sent.status",
				"  foreach step in [0..1] {",
				"    log $\"Replay step {step}\"",
				"  }",
				"}"
			];
	}

	private static string EscapeForForRestString(string value)
	{
		return (value ?? string.Empty)
			.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal);
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

	private static string NormalizeExplorerLocation(string location)
	{
		return (location ?? string.Empty)
			.Replace('\\', '/')
			.Trim()
			.TrimStart('~')
			.TrimStart('/');
	}

	private static string BuildSlug(string value, string fallback)
	{
		char[] slugCharacters = (value ?? string.Empty)
			.ToLowerInvariant()
			.Select(static character => char.IsLetterOrDigit(character) ? character : '-')
			.ToArray();

		string slug = string.Join(
			"-",
			new string(slugCharacters)
				.Split('-', StringSplitOptions.RemoveEmptyEntries));

		return string.IsNullOrWhiteSpace(slug) ? fallback : slug;
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
