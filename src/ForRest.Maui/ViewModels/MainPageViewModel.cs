using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ForRest.Licensing;
using ForRest.Domain;
using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using ForRest.Models;
using ForRest.Repositories;
using ForRest.Services;
using ForRest.Services.AI;
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
	private const double DefaultRightPanePixels = 420d;
	private const double MinLeftPanePixels = 220d;
	private const double MinRightPanePixels = 320d;
	private const double MinCenterPanePixels = 620d;
	private const double SplitterPixels = 14d;
	private const double CollapsedPaneRailPixels = 72d;
	private const double CompactLayoutBreakpoint = 980d;
	private const double CompactPaneMinWidth = 300d;
	private const double CompactPaneMaxWidth = 440d;
	private const int MaxDocumentHistoryEntries = 200;
	private static readonly TimeSpan SettingsEditorProjectionRefreshQuietPeriod = TimeSpan.FromMilliseconds(1000);
	private const string RequestDocumentKind = "request";
	private const string SettingsDocumentKind = "settings";

	private enum StashSortMode
	{
		Captured,
		PopulatedFields,
		Preview
	}

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
	private readonly IScriptEngine _scriptEngine;
	private readonly IExecutionHistoryRepository _executionHistoryRepository;
	private readonly ForRestScriptDocumentTextService _documentTextService;
	private readonly IAppActivationService _appActivationService;
	private readonly IWorkbenchAiSettingsProvider _aiSettingsProvider;
	private readonly IAiInlineConversationService _aiInlineConversationService;
	private readonly Dictionary<Guid, RequestWorkbenchWorkspaceState> _workspaceStates = [];
	private readonly Dictionary<string, DocumentTextHistory> _documentTextHistories = new(StringComparer.OrdinalIgnoreCase);
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
	private string _requestBodyText;
	private string _requestRawText;
	private string _responseState;
	private string _responseTimeStatus;
	private string _responseSizeStatus;
	private ResponseSnapshot? _latestResponseSnapshot;
	private ResponseSnapshot? _selectedInspectorResponseSnapshot;
	private RequestSnapshot? _selectedInspectorRequestSnapshot;
	private ResponseSnapshotEntryViewModel? _selectedResponseSnapshotEntry;
	private RequestSnapshotEntryViewModel? _selectedRequestSnapshotEntry;
	private string _latestRuntimeStateText = string.Empty;
	private string _latestRuntimeErrorText = string.Empty;
	private string _latestRuntimeDebugText = string.Empty;
	private bool _isResponsePrettyPrintEnabled = true;
	private bool _isSynchronizingInspectorSnapshotSelection;
	private string _debugOutputText;
	private string _executionStatus;
	private string _activationStatus;
	private string _activationDetail;
	private bool _canExecuteRequests = true;
	private bool _isStatusBannerVisible;
	private string _statusBannerTitle = string.Empty;
	private string _statusBannerDetail = string.Empty;
	private Color _statusBannerBackgroundColor = Color.FromArgb("#FDECEC");
	private Color _statusBannerBorderColor = Color.FromArgb("#D97A75");
	private Color _statusBannerTextColor = Color.FromArgb("#6F2723");
	private string _editorThemeKey;
	private ShellThemeName _currentThemeName;
	private double _activeEditorFontSize = ForRestStyleSettings.DefaultEditorFontSize;
	private double _resultPaneTabFontSize = ForRestStyleSettings.DefaultResultPaneTabFontSize;
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
	private CancellationTokenSource? _settingsProjectionRefreshSource;
	private CancellationTokenSource? _requestSaveSource;
	private CancellationTokenSource? _requestMetadataRefreshSource;
	private int _requestMetadataRefreshVersion;
	private DateTimeOffset _lastSettingsEditUtc = DateTimeOffset.MinValue;
	private bool _suppressSettingsAutosave;
	private bool _suppressRequestAutosave;
	private bool _suppressDocumentSynchronization;
	private bool _suppressDocumentHistory;
	private bool _isInitialized;
	private bool _isShuttingDown;
	private bool _isSending;
	private RequestBodyMode _requestBodyMode = RequestBodyMode.Json;
	private Guid _selectedWorkspaceId;
	private readonly IReadOnlyList<LanguageHelpEntryViewModel> _languageHelpSourceEntries;
	private string _languageHelpCatalogJson;
	private bool _isLanguageHelpOpen;
	private string _languageHelpSearchText;
	private readonly List<string> _allStashColumnTitles = [];
	private readonly List<StashRowViewModel> _allStashRows = [];
	private string _stashSearchText = string.Empty;
	private bool _hideEmptyStashColumns;
	private StashSortMode _stashSortMode = StashSortMode.Captured;
	private bool _isStashSortDescending;
	private StashRowViewModel? _selectedStashRow;
	private string _selectedLanguageHelpKey;
	private string _selectedLanguageHelpTitle;
	private string _selectedLanguageHelpCategory;
	private string _selectedLanguageHelpSummary;
	private string _selectedLanguageHelpDocumentation;
	private string _selectedLanguageHelpExample;
	private int _activeEditorLineNumber = 1;
	private int _activeEditorColumnNumber = 1;
	private int _pendingEditorCursorLineNumber;
	private int _pendingEditorCursorColumnNumber;
	private int _activeEditorRequestedCursorLineNumber;
	private int _activeEditorRequestedCursorColumn;
	private int _activeEditorRequestedCursorVersion;
	private ActivationSnapshot _latestActivationSnapshot = CreatePendingActivationSnapshot();

	public MainPageViewModel(
		IThemeService themeService,
		SettingsTomlDocumentService settingsTomlDocumentService,
		RequestWorkbenchStateStore requestWorkbenchStateStore,
		IForRestScriptExecutionService scriptExecutionService,
		IScriptEngine scriptEngine,
		IExecutionHistoryRepository executionHistoryRepository,
		ForRestScriptDocumentTextService documentTextService,
		IAppActivationService appActivationService,
		IWorkbenchAiSettingsProvider aiSettingsProvider,
		IAiInlineConversationService aiInlineConversationService)
	{
		_themeService = themeService;
		_settingsTomlDocumentService = settingsTomlDocumentService;
		_requestWorkbenchStateStore = requestWorkbenchStateStore;
		_scriptExecutionService = scriptExecutionService;
		_scriptEngine = scriptEngine;
		_executionHistoryRepository = executionHistoryRepository;
		_documentTextService = documentTextService;
		_appActivationService = appActivationService;
		_aiSettingsProvider = aiSettingsProvider;
		_aiInlineConversationService = aiInlineConversationService;
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
		_requestBodyText = string.Empty;
		_requestRawText = string.Empty;
		_responseState = "Idle";
		_responseTimeStatus = "--";
		_responseSizeStatus = "--";
		_debugOutputText = "Debug output, compile diagnostics, console entries, and exceptions appear here.";
		_executionStatus = themeService.CurrentStatusMessage;
		_activationStatus = "Activation pending";
		_activationDetail = "License state has not been evaluated yet.";
		_currentThemeName = themeService.CurrentTheme.Name;
		_editorThemeKey = themeService.CurrentTheme.MonacoThemeKey;
		_activeEditorFontSize = themeService.CurrentSettings.Style.EditorFontSize;
		_resultPaneTabFontSize = themeService.CurrentSettings.Style.ResultPaneTabFontSize;
		_themeConfigText = ReadSettingsText(_currentThemeName, _latestActivationSnapshot);
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
			new PaneTabViewModel("requests", "Requests"),
			new PaneTabViewModel("stash", "Stash"),
			new PaneTabViewModel("headers", "Headers"),
			new PaneTabViewModel("trace", "Trace"),
			new PaneTabViewModel("raw", "Raw"),
			new PaneTabViewModel("debug", "Debug")
		];

		ExplorerSections = [];

		HistoryItems =
		[];

		ResponseSnapshotEntries =
		[];

		RequestSnapshotEntries =
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

		SelectedStashDetails =
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

	public ObservableCollection<ResponseSnapshotEntryViewModel> ResponseSnapshotEntries { get; }

	public ObservableCollection<RequestSnapshotEntryViewModel> RequestSnapshotEntries { get; }

	public ObservableCollection<NameValueRowViewModel> ResponseHeaderRows { get; }

	public ObservableCollection<OutputMetricViewModel> OutputMetrics { get; }

	public ObservableCollection<TraceEntryViewModel> TraceEntries { get; }

	public ObservableCollection<StashColumnViewModel> StashColumns { get; }

	public ObservableCollection<StashRowViewModel> StashRows { get; }

	public ObservableCollection<NameValueRowViewModel> SelectedStashDetails { get; }

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
				RefreshUndoRedoState();
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

	public string RequestBodyText
	{
		get => _requestBodyText;
		private set
		{
			if (SetProperty(ref _requestBodyText, value))
			{
				OnPropertyChanged(nameof(CanCopyRequestBody));
			}
		}
	}

	public string RequestRawText
	{
		get => _requestRawText;
		private set
		{
			if (SetProperty(ref _requestRawText, value))
			{
				OnPropertyChanged(nameof(CanCopyRawRequest));
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
				RefreshRequestPresentation();
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

	public double ActiveEditorFontSize
	{
		get => _activeEditorFontSize;
		private set => SetProperty(ref _activeEditorFontSize, value);
	}

	public double ResultPaneTabFontSize
	{
		get => _resultPaneTabFontSize;
		private set => SetProperty(ref _resultPaneTabFontSize, value);
	}

	public string ActiveEditorText
	{
		get => _activeEditorText;
		set
		{
			string previousValue = _activeEditorText;

			if (!SetProperty(ref _activeEditorText, value))
			{
				return;
			}

			if (IsActiveSettingsEditor)
			{
				_lastSettingsEditUtc = DateTimeOffset.UtcNow;
				CancelPendingSettingsProjectionRefresh();
				_themeConfigText = value;
				ActiveEditorEditableRangesJson = BuildEditableRangesJson(_themeConfigText);
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

			RecordActiveDocumentHistoryChange(previousValue);
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

	public int ActiveEditorRequestedCursorLineNumber
	{
		get => _activeEditorRequestedCursorLineNumber;
		private set => SetProperty(ref _activeEditorRequestedCursorLineNumber, value);
	}

	public int ActiveEditorRequestedCursorColumn
	{
		get => _activeEditorRequestedCursorColumn;
		private set => SetProperty(ref _activeEditorRequestedCursorColumn, value);
	}

	public int ActiveEditorRequestedCursorVersion
	{
		get => _activeEditorRequestedCursorVersion;
		private set => SetProperty(ref _activeEditorRequestedCursorVersion, value);
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

	public string StashSearchText
	{
		get => _stashSearchText;
		set
		{
			if (SetProperty(ref _stashSearchText, value))
			{
				OnPropertyChanged(nameof(HasStashFilter));
				OnPropertyChanged(nameof(CanClearStashSearch));
				RefreshVisibleStashRows();
			}
		}
	}

	public bool CanClearStashSearch => !string.IsNullOrWhiteSpace(StashSearchText);

	public bool HideEmptyStashColumns => _hideEmptyStashColumns;

	public string HideEmptyStashColumnsButtonText => HideEmptyStashColumns ? "Show Empty Cols" : "Hide Empty Cols";

	public string StashSortButtonText => _stashSortMode switch
	{
		StashSortMode.Captured => "Sort: Capture",
		StashSortMode.PopulatedFields => "Sort: Filled",
		StashSortMode.Preview => "Sort: A-Z",
		_ => "Sort"
	};

	public string StashSortDirectionButtonText => _isStashSortDescending ? "Desc" : "Asc";

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

	public ResponseSnapshotEntryViewModel? SelectedResponseSnapshotEntry
	{
		get => _selectedResponseSnapshotEntry;
		set
		{
			if (!SetProperty(ref _selectedResponseSnapshotEntry, value))
			{
				return;
			}

			foreach (ResponseSnapshotEntryViewModel entry in ResponseSnapshotEntries)
			{
				entry.IsSelected = ReferenceEquals(entry, value);
			}

			ApplySelectedResponseSnapshot(value?.Snapshot);
			SynchronizeSelectedRequestSnapshot(value?.Position);
			OnPropertyChanged(nameof(SelectedResponseSnapshotSummaryText));
			OnPropertyChanged(nameof(SelectedResponseSnapshotTargetText));
			OnPropertyChanged(nameof(SelectedResponseSnapshotMetaText));
			OnPropertyChanged(nameof(CanSelectPreviousResponseSnapshot));
			OnPropertyChanged(nameof(CanSelectNextResponseSnapshot));
		}
	}

	public RequestSnapshotEntryViewModel? SelectedRequestSnapshotEntry
	{
		get => _selectedRequestSnapshotEntry;
		set
		{
			if (!SetProperty(ref _selectedRequestSnapshotEntry, value))
			{
				return;
			}

			foreach (RequestSnapshotEntryViewModel entry in RequestSnapshotEntries)
			{
				entry.IsSelected = ReferenceEquals(entry, value);
			}

			ApplySelectedRequestSnapshot(value?.Snapshot);
			SynchronizeSelectedResponseSnapshot(value?.Position);
			OnPropertyChanged(nameof(SelectedRequestSnapshotSummaryText));
			OnPropertyChanged(nameof(SelectedRequestSnapshotTargetText));
			OnPropertyChanged(nameof(SelectedRequestSnapshotMetaText));
			OnPropertyChanged(nameof(SelectedResponseSnapshotTargetText));
			OnPropertyChanged(nameof(CanSelectPreviousRequestSnapshot));
			OnPropertyChanged(nameof(CanSelectNextRequestSnapshot));
		}
	}

	public StashRowViewModel? SelectedStashRow
	{
		get => _selectedStashRow;
		set
		{
			if (!SetProperty(ref _selectedStashRow, value))
			{
				return;
			}

			foreach (StashRowViewModel row in StashRows)
			{
				row.IsSelected = ReferenceEquals(row, value);
			}

			RefreshSelectedStashDetails();
			OnPropertyChanged(nameof(HasSelectedStashRow));
			OnPropertyChanged(nameof(CanCopySelectedStashRow));
			OnPropertyChanged(nameof(SelectedStashRowTitleText));
			OnPropertyChanged(nameof(SelectedStashRowSummaryText));
			OnPropertyChanged(nameof(ShowSelectedStashRowEmptyState));
			OnPropertyChanged(nameof(ShowSelectedStashDetailsEmptyState));
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

	public GridLength LeftRestoreRailWidth => new(ShowLeftPaneRestoreButton ? CollapsedPaneRailPixels : 0d, GridUnitType.Absolute);

	public GridLength LeftPaneWidth => new(IsLeftPaneVisible ? _leftPanePixels : 0d, GridUnitType.Absolute);

	public GridLength RightRestoreRailWidth => new(ShowRightPaneRestoreButton ? CollapsedPaneRailPixels : 0d, GridUnitType.Absolute);

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

	public bool IsStatusBannerVisible
	{
		get => _isStatusBannerVisible;
		private set => SetProperty(ref _isStatusBannerVisible, value);
	}

	public string StatusBannerTitle
	{
		get => _statusBannerTitle;
		private set => SetProperty(ref _statusBannerTitle, value);
	}

	public string StatusBannerDetail
	{
		get => _statusBannerDetail;
		private set => SetProperty(ref _statusBannerDetail, value);
	}

	public Color StatusBannerBackgroundColor
	{
		get => _statusBannerBackgroundColor;
		private set => SetProperty(ref _statusBannerBackgroundColor, value);
	}

	public Color StatusBannerBorderColor
	{
		get => _statusBannerBorderColor;
		private set => SetProperty(ref _statusBannerBorderColor, value);
	}

	public Color StatusBannerTextColor
	{
		get => _statusBannerTextColor;
		private set => SetProperty(ref _statusBannerTextColor, value);
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
				OnPropertyChanged(nameof(CanUndo));
				OnPropertyChanged(nameof(CanRedo));
				OnPropertyChanged(nameof(SendButtonText));
			}
		}
	}

	public bool CanSend => !IsSending && IsActiveRequestEditor && _canExecuteRequests;

	public bool CanUndo => !IsSending && TryGetActiveDocumentHistory(out _, out DocumentTextHistory? history) && history is not null && history.CanUndo;

	public bool CanRedo => !IsSending && TryGetActiveDocumentHistory(out _, out DocumentTextHistory? history) && history is not null && history.CanRedo;

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

	public bool HasStashData => StashColumns.Count > 0 && _allStashRows.Count > 0;

	public bool HasVisibleStashRows => StashColumns.Count > 0 && StashRows.Count > 0;

	public bool ShowResponseSnapshotSelector => ResponseSnapshotEntries.Count > 1;

	public bool ShowRequestSnapshotSelector => RequestSnapshotEntries.Count > 1;

	public bool CanSelectPreviousResponseSnapshot =>
		GetSelectedResponseSnapshotEntryIndex() > 0;

	public bool CanSelectNextResponseSnapshot =>
		GetSelectedResponseSnapshotEntryIndex() is int index && index >= 0 && index < ResponseSnapshotEntries.Count - 1;

	public bool CanSelectPreviousRequestSnapshot =>
		GetSelectedRequestSnapshotEntryIndex() > 0;

	public bool CanSelectNextRequestSnapshot =>
		GetSelectedRequestSnapshotEntryIndex() is int index && index >= 0 && index < RequestSnapshotEntries.Count - 1;

	public bool ShowStashEmptyState => !HasStashData;

	public bool ShowStashFilterEmptyState => HasStashData && !HasVisibleStashRows;

	public bool HasStashFilter => !string.IsNullOrWhiteSpace(StashSearchText);

	public bool CanCopyResponseBody => !string.IsNullOrWhiteSpace(ResponseBodyText);

	public bool CanCopyRawResponse => !string.IsNullOrWhiteSpace(ResponseRawText);

	public bool CanCopyRequestBody => !string.IsNullOrWhiteSpace(RequestBodyText);

	public bool CanCopyRawRequest => !string.IsNullOrWhiteSpace(RequestRawText);

	public bool CanCopyDebugOutput => !string.IsNullOrWhiteSpace(DebugOutputText);

	public bool CanCopyHeaders => ResponseHeaderRows.Count > 0;

	public bool CanCopyTrace => TraceEntries.Count > 0;

	public bool CanCopyStash => HasVisibleStashRows;

	public bool CanExportStashCsv => HasVisibleStashRows;

	public bool HasSelectedStashRow => SelectedStashRow is not null;

	public bool CanCopySelectedStashRow => SelectedStashRow is not null;

	public bool ShowSelectedStashRowEmptyState => HasVisibleStashRows && !HasSelectedStashRow;

	public bool ShowSelectedStashDetailsEmptyState => HasSelectedStashRow && SelectedStashDetails.Count == 0;

	public bool ShowDesktopStashTable => HasVisibleStashRows && IsDesktopLayout;

	public bool ShowCompactStashCards => HasVisibleStashRows && IsCompactLayout;

	public bool ShowDesktopSelectedStashPanel => HasSelectedStashRow && IsDesktopLayout;

	public string StashEmptyStateText => "No stash rows were captured for the current run. Flow code must execute stash writes before the run ends; lines skipped by break, continue, or return do not contribute rows.";

	public string StashFilterEmptyStateText => string.IsNullOrWhiteSpace(StashSearchText)
		? "No visible stash rows."
		: $"No stash rows match \"{StashSearchText}\".";

	public string StashSummaryText
	{
		get
		{
			int totalRowCount = _allStashRows.Count;
			int visibleColumnCount = StashColumns.Count;
			int totalColumnCount = _allStashColumnTitles.Count;
			if (totalRowCount == 0 || totalColumnCount == 0)
			{
				return "No rows captured";
			}

			string rowLabel = totalRowCount == 1 ? "row" : "rows";
			string columnText = HideEmptyStashColumns && visibleColumnCount != totalColumnCount
				? $"{visibleColumnCount}/{totalColumnCount} columns visible"
				: $"{visibleColumnCount} {(visibleColumnCount == 1 ? "column" : "columns")}";
			return $"{totalRowCount} {rowLabel}  {columnText}";
		}
	}

	public string StashFilterSummaryText
	{
		get
		{
			if (!HasStashData)
			{
				return "Run a script that commits stash rows to populate this table.";
			}

			string sortText = _stashSortMode switch
			{
				StashSortMode.Captured => _isStashSortDescending ? "capture desc" : "capture asc",
				StashSortMode.PopulatedFields => _isStashSortDescending ? "filled desc" : "filled asc",
				StashSortMode.Preview => _isStashSortDescending ? "A-Z desc" : "A-Z asc",
				_ => "capture asc"
			};

			string emptyColumnText = HideEmptyStashColumns ? "  Empty columns hidden." : string.Empty;
			if (string.IsNullOrWhiteSpace(StashSearchText))
			{
				return $"Showing all captured rows.  Sorted by {sortText}.{emptyColumnText}";
			}

			string visibleLabel = StashRows.Count == 1 ? "row" : "rows";
			string totalLabel = _allStashRows.Count == 1 ? "row" : "rows";
			return $"Showing {StashRows.Count} {visibleLabel} of {_allStashRows.Count} {totalLabel}.  Sorted by {sortText}.{emptyColumnText}";
		}
	}

	public string SelectedStashRowTitleText => SelectedStashRow is null
		? "No row selected"
		: $"Row {SelectedStashRow.RowLabel}";

	public string SelectedStashRowSummaryText => SelectedStashRow?.DetailSummaryText ?? string.Empty;

	public string SelectedResponseSnapshotSummaryText
	{
		get
		{
			if (SelectedResponseSnapshotEntry is null)
			{
				return "No response captured for this run yet.";
			}

			string summary = $"{SelectedResponseSnapshotEntry.StatusText}  {SelectedResponseSnapshotEntry.DetailText}".Trim();
			return ResponseSnapshotEntries.Count > 1
				? $"Send {SelectedResponseSnapshotEntry.Position} of {ResponseSnapshotEntries.Count}  {summary}"
				: summary;
		}
	}

	public string SelectedResponseSnapshotTargetText
	{
		get
		{
			if (SelectedResponseSnapshotEntry is null)
			{
				return "No response captured for this run yet.";
			}

			RequestSnapshot? request = ResolveRequestSnapshotForSelectedResponse();
			return request is null
				? "Request details are unavailable for this response."
				: $"{request.Method}  {request.Url}";
		}
	}

	public string SelectedResponseSnapshotMetaText
	{
		get
		{
			if (SelectedResponseSnapshotEntry?.Snapshot is not { } response)
			{
				return string.Empty;
			}

			List<string> parts = [];
			if (!string.IsNullOrWhiteSpace(response.ContentType))
			{
				parts.Add(response.ContentType);
			}

			parts.Add($"{response.DurationMilliseconds} ms");
			parts.Add(FormatResponseSize(response.SizeBytes));
			parts.Add($"Received {response.ReceivedUtc.ToLocalTime():T}");
			return string.Join("  ", parts);
		}
	}

	public string SelectedRequestSnapshotSummaryText
	{
		get
		{
			if (SelectedRequestSnapshotEntry is null)
			{
				return ResponseSnapshotEntries.Count > 1
					? "Captured requests are unavailable for this multi-send run."
					: "No request captured for this run yet.";
			}

			string summary = $"{SelectedRequestSnapshotEntry.MethodText}  {SelectedRequestSnapshotEntry.DetailText}".Trim();
			return RequestSnapshotEntries.Count > 1
				? $"Send {SelectedRequestSnapshotEntry.Position} of {RequestSnapshotEntries.Count}  {summary}"
				: summary;
		}
	}

	public string SelectedRequestSnapshotTargetText
	{
		get
		{
			if (SelectedRequestSnapshotEntry?.Snapshot is not { } request)
			{
				return ResponseSnapshotEntries.Count > 1
					? "Requests were not captured for every send in this run."
					: "No request captured for this run yet.";
			}

			return $"{request.Method}  {request.Url}";
		}
	}

	public string SelectedRequestSnapshotMetaText
	{
		get
		{
			if (SelectedRequestSnapshotEntry?.Snapshot is not { } request)
			{
				return string.Empty;
			}

			List<string> parts = [];
			if (!string.IsNullOrWhiteSpace(request.ContentType))
			{
				parts.Add(request.ContentType);
			}

			parts.Add(FormatResponseSize(request.SizeBytes));
			parts.Add($"{request.Headers.Count(static item => item.IsEnabled)} headers");
			parts.Add($"Sent {request.SentUtc.ToLocalTime():T}");
			return string.Join("  ", parts);
		}
	}

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
		"requests" => "Captured request viewer",
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

	public bool IsInspectorRequestVisible => IsTabSelected(RightPaneTabs, "requests");

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
		await RefreshActivationStatusAsync();
		_isInitialized = true;
	}

	public async Task PrepareForShutdownAsync()
	{
		if (_isShuttingDown)
		{
			return;
		}

		_isShuttingDown = true;
		CancelPendingRequestMetadataRefresh();
		CancelPendingRequestAutosave();
		CancelPendingSettingsAutosave();
		CancelPendingSettingsProjectionRefresh();

		try
		{
			if (!string.IsNullOrWhiteSpace(RequestLocation))
			{
				await PersistCurrentRequestAsync();
			}
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Request shutdown persistence failed.", exception);
		}

		try
		{
			if (_settingsTomlDocumentService.CanAutoSave(_themeConfigText))
			{
				_settingsTomlDocumentService.SaveRawText(_themeConfigText);
			}
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Settings shutdown persistence failed.", exception);
		}
	}

	public async Task SendAsync()
	{
		if (!IsActiveRequestEditor || IsSending)
		{
			return;
		}

		await RefreshActivationStatusAsync();
		if (!_canExecuteRequests)
		{
			ApplyActivationBlock();
			return;
		}

		AiSettings aiSettings = _aiSettingsProvider.GetCurrentSettings();
		if (await TryHandleInlineAiAsync(aiSettings))
		{
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
				ApplyEditorDebugSnapshot(
					CreateRequestEditorDebugSnapshot(outcome.Compilation, executionSource),
					updateDebugOutput: false);
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
			if (outcome.Execution is not null)
			{
				RecordLatestRuntimeContext(
					outcome.Execution.State.ToString(),
					latestRun?.ErrorMessage,
					DebugOutputText);
			}
			else
			{
				ClearLatestRuntimeContext();
			}

			List<ResponseSnapshot> capturedResponses = BuildCapturedResponses(latestRun, outcome.Execution?.LatestResponse);
			ApplyRequestSnapshotEntries(BuildCapturedRequests(latestRun, capturedResponses.Count));
			ApplyResponseSnapshotEntries(capturedResponses);

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
			ApplyEditorDebugSnapshot(
				CreateRequestEditorDebugSnapshot(outcome.Compilation, executionSource),
				updateDebugOutput: false);

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
			ApplyRequestSnapshotEntries([]);
			ApplyResponseSnapshotEntries([]);
			DebugOutputText = exception.ToString();
			RecordLatestRuntimeContext("Failed", exception.Message, DebugOutputText);
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

		ConstrainPaneLayout(_workbenchWidth);
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

		ConstrainPaneLayout(_workbenchWidth);
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
		OnPropertyChanged(nameof(ShowDesktopStashTable));
		OnPropertyChanged(nameof(ShowCompactStashCards));
		OnPropertyChanged(nameof(ShowDesktopSelectedStashPanel));

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
		OnPropertyChanged(nameof(IsInspectorRequestVisible));
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
		RekeyWorkspaceDocumentHistory(currentWorkspace, renamedWorkspace);
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
		ClearWorkspaceDocumentHistory(workspace);
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

		ClearDocumentHistory(BuildRequestHistoryKey(workspace.Id, workspace.Documents[currentIndex].Location));

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
		UpdateRequestMetadataFromSource();
		SelectExplorerItemByContext(document.Location);
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
		UpdateRequestMetadataFromSource();
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

	public void Undo()
	{
		if (IsSending)
		{
			return;
		}

		if (!TryGetActiveDocumentHistory(out _, out DocumentTextHistory? history) || history is null)
		{
			RefreshUndoRedoState();
			return;
		}

		string currentText = GetActiveDocumentTextSnapshot();
		if (!history.TryUndo(currentText, out string previousText))
		{
			RefreshUndoRedoState();
			return;
		}

		ApplyHistoryDocumentText(previousText);
		RefreshUndoRedoState();
	}

	public void Redo()
	{
		if (IsSending)
		{
			return;
		}

		if (!TryGetActiveDocumentHistory(out _, out DocumentTextHistory? history) || history is null)
		{
			RefreshUndoRedoState();
			return;
		}

		string currentText = GetActiveDocumentTextSnapshot();
		if (!history.TryRedo(currentText, out string nextText))
		{
			RefreshUndoRedoState();
			return;
		}

		ApplyHistoryDocumentText(nextText);
		RefreshUndoRedoState();
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

	public async Task CopyRequestBodyAsync()
	{
		if (!CanCopyRequestBody)
		{
			return;
		}

		await Clipboard.Default.SetTextAsync(RequestBodyText);
		ExecutionStatus = "Copied request body.";
	}

	public async Task CopyRawRequestAsync()
	{
		if (!CanCopyRawRequest)
		{
			return;
		}

		await Clipboard.Default.SetTextAsync(RequestRawText);
		ExecutionStatus = "Copied raw request.";
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

	public async Task CopySelectedStashRowAsync()
	{
		if (!CanCopySelectedStashRow || SelectedStashRow is null)
		{
			ExecutionStatus = "No stash row selected to copy.";
			return;
		}

		string text = ResponsePaneCopyFormatter.BuildStashRowText(StashColumns, SelectedStashRow);
		await Clipboard.Default.SetTextAsync(text);
		ExecutionStatus = $"Copied stash row {SelectedStashRow.RowLabel}.";
	}

	public async Task ExportStashCsvAsync()
	{
		if (!CanExportStashCsv)
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

	public void SelectStashRow(StashRowViewModel? row)
	{
		if (row is not null && !_allStashRows.Contains(row))
		{
			return;
		}

		SelectedStashRow = row;
	}

	public void ClearStashFilter()
	{
		if (string.IsNullOrEmpty(StashSearchText))
		{
			return;
		}

		StashSearchText = string.Empty;
	}

	public void CycleStashSortMode()
	{
		_stashSortMode = _stashSortMode switch
		{
			StashSortMode.Captured => StashSortMode.PopulatedFields,
			StashSortMode.PopulatedFields => StashSortMode.Preview,
			_ => StashSortMode.Captured,
		};

		OnPropertyChanged(nameof(StashSortButtonText));
		RefreshVisibleStashRows();
	}

	public void ToggleStashSortDirection()
	{
		_isStashSortDescending = !_isStashSortDescending;
		OnPropertyChanged(nameof(StashSortDirectionButtonText));
		RefreshVisibleStashRows();
	}

	public void ToggleHideEmptyStashColumns()
	{
		_hideEmptyStashColumns = !_hideEmptyStashColumns;
		OnPropertyChanged(nameof(HideEmptyStashColumns));
		OnPropertyChanged(nameof(HideEmptyStashColumnsButtonText));
		RefreshVisibleStashRows();
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

		_allStashColumnTitles.Clear();
		_allStashColumnTitles.AddRange(orderedColumns);

		if (!string.IsNullOrEmpty(_stashSearchText))
		{
			_stashSearchText = string.Empty;
			OnPropertyChanged(nameof(StashSearchText));
			OnPropertyChanged(nameof(HasStashFilter));
			OnPropertyChanged(nameof(CanClearStashSearch));
		}

		_stashSortMode = StashSortMode.Captured;
		_isStashSortDescending = false;
		OnPropertyChanged(nameof(StashSortButtonText));
		OnPropertyChanged(nameof(StashSortDirectionButtonText));

		_allStashRows.Clear();
		int rowNumber = 1;
		foreach (StashRow row in stash.Rows)
		{
			List<StashCellViewModel> cells = [];
			List<NameValueRowViewModel> details = [];
			StringBuilder searchBuilder = new();
			searchBuilder.Append(rowNumber.ToString(CultureInfo.InvariantCulture));

			foreach (string column in orderedColumns)
			{
				string value = row.Values.TryGetValue(column, out string? rawValue)
					? rawValue
					: string.Empty;
				StashCellViewModel cell = new(value, column);
				cells.Add(cell);

				searchBuilder.Append(' ').Append(column);
				if (!cell.IsEmpty)
				{
					searchBuilder.Append(' ').Append(value);
					details.Add(new NameValueRowViewModel(column, value, "stash"));
				}
			}

			_allStashRows.Add(
				new StashRowViewModel(
					rowNumber,
					cells,
					details,
					isAlternate: rowNumber % 2 == 0,
					searchBuilder.ToString()));
			rowNumber++;
		}

		RefreshVisibleStashRows();
	}

	private void ClearStashTable()
	{
		_allStashColumnTitles.Clear();
		_allStashRows.Clear();
		StashColumns.Clear();
		StashRows.Clear();
		SelectedStashDetails.Clear();
		if (!string.IsNullOrEmpty(_stashSearchText))
		{
			_stashSearchText = string.Empty;
			OnPropertyChanged(nameof(StashSearchText));
			OnPropertyChanged(nameof(HasStashFilter));
			OnPropertyChanged(nameof(CanClearStashSearch));
		}

		SelectedStashRow = null;
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

	private void RefreshVisibleStashRows()
	{
		string query = StashSearchText.Trim();
		List<StashRowViewModel> visibleSourceRows =
		[
			.. (string.IsNullOrWhiteSpace(query)
			? _allStashRows
			: _allStashRows.Where(
				row => row.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)))
		];

		IReadOnlyList<int> visibleColumnIndexes = ResolveVisibleStashColumnIndexes(visibleSourceRows);

		IEnumerable<StashRowViewModel> orderedRows = ApplyStashSorting(visibleSourceRows);

		int? selectedRowNumber = SelectedStashRow?.RowNumber;

		StashColumns.Clear();
		foreach (int columnIndex in visibleColumnIndexes)
		{
			string columnTitle = _allStashColumnTitles[columnIndex];
			int populatedValueCount = visibleSourceRows.Count(row => columnIndex < row.Cells.Count && !row.Cells[columnIndex].IsEmpty);
			StashColumns.Add(new StashColumnViewModel(columnTitle, populatedValueCount));
		}

		StashRows.Clear();
		int visibleRowIndex = 0;
		foreach (StashRowViewModel sourceRow in orderedRows)
		{
			List<StashCellViewModel> cells = [];
			List<NameValueRowViewModel> details = [];
			foreach (int columnIndex in visibleColumnIndexes)
			{
				StashCellViewModel sourceCell = sourceRow.Cells[columnIndex];
				string columnTitle = _allStashColumnTitles[columnIndex];
				cells.Add(new StashCellViewModel(sourceCell.Value, columnTitle));
				if (!sourceCell.IsEmpty)
				{
					details.Add(new NameValueRowViewModel(columnTitle, sourceCell.Value, "stash"));
				}
			}

			StashRows.Add(
				new StashRowViewModel(
					sourceRow.RowNumber,
					cells,
					details,
					isAlternate: visibleRowIndex % 2 == 1,
					sourceRow.SearchText));
			visibleRowIndex++;
		}

		if (selectedRowNumber is int rowNumber &&
			StashRows.FirstOrDefault(row => row.RowNumber == rowNumber) is { } matchedRow)
		{
			SelectedStashRow = matchedRow;
		}
		else if (SelectedStashRow is null || !StashRows.Contains(SelectedStashRow))
		{
			SelectedStashRow = StashRows.FirstOrDefault();
		}
		else
		{
			RefreshSelectedStashDetails();
		}

		NotifyStashStateChanged();
	}

	private void RefreshSelectedStashDetails()
	{
		SelectedStashDetails.Clear();
		if (SelectedStashRow is null)
		{
			OnPropertyChanged(nameof(ShowSelectedStashDetailsEmptyState));
			return;
		}

		foreach (NameValueRowViewModel detail in SelectedStashRow.Details)
		{
			SelectedStashDetails.Add(detail);
		}

		OnPropertyChanged(nameof(ShowSelectedStashDetailsEmptyState));
	}

	private void NotifyStashStateChanged()
	{
		OnPropertyChanged(nameof(HasStashData));
		OnPropertyChanged(nameof(HasVisibleStashRows));
		OnPropertyChanged(nameof(ShowStashEmptyState));
		OnPropertyChanged(nameof(ShowStashFilterEmptyState));
		OnPropertyChanged(nameof(ShowSelectedStashRowEmptyState));
		OnPropertyChanged(nameof(ShowSelectedStashDetailsEmptyState));
		OnPropertyChanged(nameof(StashFilterEmptyStateText));
		OnPropertyChanged(nameof(CanCopyStash));
		OnPropertyChanged(nameof(CanExportStashCsv));
		OnPropertyChanged(nameof(CanCopySelectedStashRow));
		OnPropertyChanged(nameof(HasSelectedStashRow));
		OnPropertyChanged(nameof(SelectedStashRowTitleText));
		OnPropertyChanged(nameof(SelectedStashRowSummaryText));
		OnPropertyChanged(nameof(ShowDesktopStashTable));
		OnPropertyChanged(nameof(ShowCompactStashCards));
		OnPropertyChanged(nameof(ShowDesktopSelectedStashPanel));
		OnPropertyChanged(nameof(HideEmptyStashColumnsButtonText));
		OnPropertyChanged(nameof(StashSortButtonText));
		OnPropertyChanged(nameof(StashSortDirectionButtonText));
		OnPropertyChanged(nameof(StashSummaryText));
		OnPropertyChanged(nameof(StashFilterSummaryText));
	}

	private IReadOnlyList<int> ResolveVisibleStashColumnIndexes(IReadOnlyList<StashRowViewModel> visibleRows)
	{
		List<int> indexes = [];
		for (int index = 0; index < _allStashColumnTitles.Count; index++)
		{
			bool hasVisibleValue = visibleRows.Any(row => index < row.Cells.Count && !row.Cells[index].IsEmpty);
			if (!_hideEmptyStashColumns || hasVisibleValue)
			{
				indexes.Add(index);
			}
		}

		if (indexes.Count == 0)
		{
			indexes.AddRange(Enumerable.Range(0, _allStashColumnTitles.Count));
		}

		return indexes;
	}

	private IEnumerable<StashRowViewModel> ApplyStashSorting(IEnumerable<StashRowViewModel> rows)
	{
		Func<StashRowViewModel, object> keySelector = _stashSortMode switch
		{
			StashSortMode.Captured => static row => row.RowNumber,
			StashSortMode.PopulatedFields => static row => row.PopulatedCellCount,
			StashSortMode.Preview => static row => row.PreviewText,
			_ => static row => row.RowNumber,
		};

		return _isStashSortDescending
			? rows.OrderByDescending(keySelector).ThenByDescending(static row => row.RowNumber)
			: rows.OrderBy(keySelector).ThenBy(static row => row.RowNumber);
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
		_activeEditorColumnNumber = Math.Max(1, column);
	}

	public bool TryConsumePendingEditorCursorRequest(out int lineNumber, out int column)
	{
		lineNumber = _pendingEditorCursorLineNumber;
		column = _pendingEditorCursorColumnNumber;
		_pendingEditorCursorLineNumber = 0;
		_pendingEditorCursorColumnNumber = 0;
		return lineNumber > 0 && column > 0;
	}

	private void RecordActiveDocumentHistoryChange(string previousEditorText)
	{
		if (_suppressDocumentHistory)
		{
			return;
		}

		string? documentKey = GetActiveDocumentHistoryKey();
		if (string.IsNullOrWhiteSpace(documentKey))
		{
			return;
		}

		RecordDocumentTextChange(documentKey, previousEditorText, GetActiveDocumentTextSnapshot());
	}

	private void RecordDocumentTextChange(string? documentKey, string previousText, string currentText)
	{
		if (_suppressDocumentHistory || string.IsNullOrWhiteSpace(documentKey))
		{
			return;
		}

		string normalizedPrevious = NormalizeLineEndings(previousText);
		string normalizedCurrent = NormalizeLineEndings(currentText);
		if (string.Equals(normalizedPrevious, normalizedCurrent, StringComparison.Ordinal))
		{
			return;
		}

		GetOrCreateDocumentHistory(documentKey).RecordChange(normalizedPrevious, normalizedCurrent);
		RefreshUndoRedoState();
	}

	private DocumentTextHistory GetOrCreateDocumentHistory(string documentKey)
	{
		if (_documentTextHistories.TryGetValue(documentKey, out DocumentTextHistory? history))
		{
			return history;
		}

		history = new DocumentTextHistory(MaxDocumentHistoryEntries);
		_documentTextHistories[documentKey] = history;
		return history;
	}

	private bool TryGetActiveDocumentHistory(out string documentKey, out DocumentTextHistory? history)
	{
		documentKey = GetActiveDocumentHistoryKey() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(documentKey))
		{
			history = null;
			return false;
		}

		return _documentTextHistories.TryGetValue(documentKey, out history);
	}

	private string? GetActiveDocumentHistoryKey()
	{
		if (IsActiveRequestEditor)
		{
			return BuildRequestHistoryKey(_selectedWorkspaceId, RequestLocation);
		}

		if (IsActiveSettingsEditor)
		{
			return BuildSettingsHistoryKey();
		}

		return null;
	}

	private static string? BuildRequestHistoryKey(Guid workspaceId, string? location)
	{
		if (workspaceId == Guid.Empty || string.IsNullOrWhiteSpace(location))
		{
			return null;
		}

		return $"request:{workspaceId:N}:{NormalizeExplorerLocation(location)}";
	}

	private string BuildSettingsHistoryKey()
	{
		return $"settings:{_settingsTomlDocumentService.ConfigFilePath}";
	}

	private string GetActiveDocumentTextSnapshot()
	{
		if (IsActiveSettingsEditor)
		{
			return NormalizeLineEndings(_themeConfigText);
		}

		if (IsActiveRequestEditor)
		{
			return NormalizeLineEndings(_requestEditorText);
		}

		return NormalizeLineEndings(_activeEditorText);
	}

	private void ApplyHistoryDocumentText(string documentText)
	{
		_suppressDocumentHistory = true;
		try
		{
			string normalized = NormalizeLineEndings(documentText);
			if (IsActiveSettingsEditor)
			{
				ActiveEditorText = normalized;
				ActiveEditorEditableRangesJson = BuildEditableRangesJson(_themeConfigText);
				return;
			}

			if (IsActiveRequestEditor)
			{
				ApplyRequestDocumentText(
					normalized,
					updateActiveEditor: true,
					forceActiveEditorRefresh: true,
					recordHistory: false,
					reconcileIdentity: false,
					scheduleAutosave: true);
			}
		}
		finally
		{
			_suppressDocumentHistory = false;
		}
	}

	private void RefreshUndoRedoState()
	{
		OnPropertyChanged(nameof(CanUndo));
		OnPropertyChanged(nameof(CanRedo));
	}

	private void ClearDocumentHistory(string? documentKey)
	{
		if (string.IsNullOrWhiteSpace(documentKey))
		{
			return;
		}

		if (_documentTextHistories.Remove(documentKey))
		{
			RefreshUndoRedoState();
		}
	}

	private void ClearWorkspaceDocumentHistory(RequestWorkbenchWorkspaceState workspace)
	{
		bool removedAny = false;
		foreach (RequestWorkbenchDocumentState document in workspace.Documents)
		{
			string? documentKey = BuildRequestHistoryKey(workspace.Id, document.Location);
			if (string.IsNullOrWhiteSpace(documentKey))
			{
				continue;
			}

			removedAny |= _documentTextHistories.Remove(documentKey);
		}

		if (removedAny)
		{
			RefreshUndoRedoState();
		}
	}

	private void RekeyDocumentHistory(string? previousDocumentKey, string? nextDocumentKey)
	{
		if (string.IsNullOrWhiteSpace(previousDocumentKey) ||
		    string.IsNullOrWhiteSpace(nextDocumentKey) ||
		    string.Equals(previousDocumentKey, nextDocumentKey, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (_documentTextHistories.Remove(previousDocumentKey, out DocumentTextHistory? history))
		{
			_documentTextHistories[nextDocumentKey] = history;
			RefreshUndoRedoState();
		}
	}

	private void RekeyWorkspaceDocumentHistory(RequestWorkbenchWorkspaceState previousWorkspace, RequestWorkbenchWorkspaceState nextWorkspace)
	{
		int documentCount = Math.Min(previousWorkspace.Documents.Count, nextWorkspace.Documents.Count);
		for (int index = 0; index < documentCount; index++)
		{
			RekeyDocumentHistory(
				BuildRequestHistoryKey(previousWorkspace.Id, previousWorkspace.Documents[index].Location),
				BuildRequestHistoryKey(nextWorkspace.Id, nextWorkspace.Documents[index].Location));
		}
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
		ApplyStyleSettings(e.Settings.Style);
		ExecutionStatus = e.StatusMessage;
		ApplyThemePalette(e.Theme);
		if (!e.IsPreview)
		{
			RequestSettingsProjectionRefresh(_currentThemeName, _latestActivationSnapshot);
		}

		_ = RefreshActivationStatusAsync();
	}

	private void ApplyStyleSettings(ForRestStyleSettings style)
	{
		ActiveEditorFontSize = style.EditorFontSize;
		ResultPaneTabFontSize = style.ResultPaneTabFontSize;
	}

	public void DismissStatusBanner()
	{
		IsStatusBannerVisible = false;
	}

	private void ShowStatusBanner(string title, string detail, bool isWarning)
	{
		StatusBannerTitle = string.IsNullOrWhiteSpace(title) ? "Notice" : title.Trim();
		StatusBannerDetail = string.IsNullOrWhiteSpace(detail) ? string.Empty : detail.Trim();
		if (isWarning)
		{
			StatusBannerBackgroundColor = Color.FromArgb("#FFF6E6");
			StatusBannerBorderColor = Color.FromArgb("#D8B46E");
			StatusBannerTextColor = Color.FromArgb("#6A5034");
		}
		else
		{
			StatusBannerBackgroundColor = Color.FromArgb("#FDECEC");
			StatusBannerBorderColor = Color.FromArgb("#D97A75");
			StatusBannerTextColor = Color.FromArgb("#6F2723");
		}

		IsStatusBannerVisible = true;
	}

	private async Task RefreshActivationStatusAsync()
	{
		try
		{
			ActivationSnapshot snapshot = await _appActivationService.EvaluateNowAsync();
			_latestActivationSnapshot = snapshot;
			ActivationStatus = snapshot.StatusText;
			ActivationDetail = snapshot.DetailText;
			_canExecuteRequests = snapshot.CanExecuteRequests;
			if (!snapshot.CanExecuteRequests ||
				snapshot.State is LicenseAccessStatus.ActivationRequired or LicenseAccessStatus.LeaseExpired or LicenseAccessStatus.Revoked or LicenseAccessStatus.Invalid or LicenseAccessStatus.ClockTampering)
			{
				ShowStatusBanner(snapshot.StatusText, snapshot.DetailText, isWarning: false);
			}
			RequestSettingsProjectionRefresh(_currentThemeName, snapshot);
		}
		catch (Exception exception)
		{
			ActivationStatus = "Activation unavailable";
			ActivationDetail = exception.Message;
			_canExecuteRequests = true;
			_latestActivationSnapshot = CreateUnavailableActivationSnapshot(exception.Message);
			ShowStatusBanner("Activation unavailable", exception.Message, isWarning: false);
			AppLaunchGuard.RecordException("Activation status refresh failed.", exception);
			RequestSettingsProjectionRefresh(_currentThemeName, _latestActivationSnapshot);
		}

		OnPropertyChanged(nameof(CanSend));
	}

	private void ApplyActivationBlock()
	{
		ResponseState = "Blocked";
		ExecutionStatus = ActivationStatus;
		ShowStatusBanner(ActivationStatus, ActivationDetail, isWarning: false);
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
		ApplyRequestSnapshotEntries([]);
		ApplyResponseSnapshotEntries([]);
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
			"PATCH" => _methodPut,
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
		ClearLatestRuntimeContext();

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
		RefreshUndoRedoState();
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
		_themeConfigText = ReadSettingsText(_currentThemeName, _latestActivationSnapshot);
		ActiveDocumentKindLabel = item.Kind;
		ActiveDocumentKindColor = item.AccentColor;
		ActiveDocumentLabel = item.Title;
		ActiveEditorLanguage = item.EditorLanguage;
		ActiveEditorEditableRangesJson = BuildEditableRangesJson(_themeConfigText);
		ActiveEditorDiagnosticsJson = "[]";
		SetActiveEditorTextInternal(_themeConfigText);
		ForceActiveEditorRefresh(includeText: false);
		RefreshUndoRedoState();
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
		ForceActiveEditorRefresh(includeText: false);
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
		if (_isShuttingDown)
		{
			return;
		}

		if (!ShouldDebounceRequestMetadataRefresh())
		{
			CancelPendingRequestMetadataRefresh();
			UpdateRequestMetadataFromSource();
			return;
		}

		int refreshVersion = Interlocked.Increment(ref _requestMetadataRefreshVersion);
		string source = _requestEditorText;
		string compilationSource = BuildConversationIgnoredRequestCompilationSource(source);
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
						compilationSource,
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

	private void CancelPendingRequestAutosave()
	{
		CancellationTokenSource? pendingSource = Interlocked.Exchange(ref _requestSaveSource, null);
		pendingSource?.Cancel();
		pendingSource?.Dispose();
	}

	private void CancelPendingSettingsAutosave()
	{
		CancellationTokenSource? pendingSource = Interlocked.Exchange(ref _settingsSaveSource, null);
		pendingSource?.Cancel();
		pendingSource?.Dispose();
	}

	private void CancelPendingSettingsProjectionRefresh()
	{
		CancellationTokenSource? pendingSource = Interlocked.Exchange(ref _settingsProjectionRefreshSource, null);
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
			ForRestScriptEditableSections sections = _documentTextService.Extract(BuildConversationFreeRequestSource(_requestEditorText));
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
			string compilationSource = BuildConversationIgnoredRequestCompilationSource(_requestEditorText);
			ForRestScriptCompilationResult compilation = _scriptExecutionService.Compile(
				compilationSource,
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

	private async Task<bool> TryHandleInlineAiAsync(AiSettings? aiSettings = null)
	{
		aiSettings ??= _aiSettingsProvider.GetCurrentSettings();
		string originalSource = RequestEditorText;
		AiInlineConversationPrompt? prompt = AiInlineConversationPromptResolver.ResolveActionablePrompt(
			originalSource,
			_activeEditorLineNumber);
		CancellationTokenSource? workingAnimationSource = null;
		Task? workingAnimationTask = null;
		CancellationTokenSource? aiRequestTimeoutSource = null;
		AiInlineConversationRequest request = new(
			DocumentId: RequestLocation,
			DocumentTitle: RequestName,
			Language: ActiveEditorLanguage,
			SourceText: originalSource,
			CursorLineNumber: _activeEditorLineNumber,
			Settings: aiSettings,
			ActiveDocumentHost: new ActiveRequestDocumentHost(this, originalSource));

		if (prompt is not null)
		{
			workingAnimationSource = new CancellationTokenSource();
			workingAnimationTask = RunInlineAiWorkingAnimationAsync(originalSource, prompt.LineNumber, workingAnimationSource.Token);
		}

		IsSending = true;
		try
		{
			int aiTimeoutSeconds = Math.Max(1, aiSettings.Conversation.ExecutionTimeoutSeconds);
			aiRequestTimeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(aiTimeoutSeconds));
			AiInlineConversationResult result = await _aiInlineConversationService.TryHandleAsync(request, aiRequestTimeoutSource.Token);
			await StopInlineAiWorkingAnimationAsync(workingAnimationSource, workingAnimationTask);
			workingAnimationSource = null;
			workingAnimationTask = null;
			if (!result.Handled)
			{
				RestoreTransientAiConversationText(originalSource);
				return false;
			}

			if (CanStreamInlineAiResponse(aiSettings, prompt, result))
			{
				try
				{
					await AnimateInlineAiResponseAsync(prompt!.LineNumber, result.ResponseText);
				}
				catch (Exception streamingException)
				{
					AppLaunchGuard.RecordException("Inline AI streaming animation failed.", streamingException);
				}
			}

			(string repairedUpdatedText, int? repairedSuggestedCursorLineNumber, int repairedSuggestedCursorColumn) = prompt is null
				? (result.UpdatedText, result.SuggestedCursorLineNumber, result.SuggestedCursorColumn)
				: RepairInlineAiConversationResult(prompt, result);
			ApplyAiConversationText(
				repairedUpdatedText,
				repairedSuggestedCursorLineNumber,
				repairedSuggestedCursorColumn,
				originalSource);

			await PersistCurrentRequestAsync();
			ExecutionStatus = result.StatusText;
			DebugOutputText = result.DebugText;
			WriteInlineAiDebugTrace(DebugOutputText);
			TraceEntries.Clear();
			TraceEntries.Add(new TraceEntryViewModel("ai", result.StatusText, DateTime.Now.ToString("T"), result.Succeeded ? _successColor : _warningColor));
			OnPropertyChanged(nameof(CanCopyTrace));
			if (!result.Succeeded)
			{
				FocusRightPaneTab("debug");
				RevealInspectorOnCompactLayout();
			}

			return true;
		}
		catch (OperationCanceledException) when (aiRequestTimeoutSource?.IsCancellationRequested == true)
		{
			await StopInlineAiWorkingAnimationAsync(workingAnimationSource, workingAnimationTask);
			workingAnimationSource = null;
			workingAnimationTask = null;
			RestoreTransientAiConversationText(originalSource);
			ExecutionStatus = "AI request timed out.";
			DebugOutputText = BuildInlineAiTimeoutDebugOutput(originalSource, prompt, aiSettings);
			WriteInlineAiDebugTrace(DebugOutputText);
			TraceEntries.Clear();
			TraceEntries.Add(new TraceEntryViewModel("ai", "AI request timed out.", DateTime.Now.ToString("T"), _dangerColor));
			if (prompt is not null)
			{
				TraceEntries.Add(new TraceEntryViewModel("prompt", $"line {prompt.LineNumber}: {BuildInlineAiTraceSummary(prompt.PromptText)}", DateTime.Now.ToString("T"), _warningColor));
			}
			TraceEntries.Add(new TraceEntryViewModel("script", "Attempted request source captured in debug output.", DateTime.Now.ToString("T"), _methodNeutral));
			OnPropertyChanged(nameof(CanCopyTrace));
			FocusRightPaneTab("debug");
			RevealInspectorOnCompactLayout();
			return true;
		}
		catch (Exception exception)
		{
			await StopInlineAiWorkingAnimationAsync(workingAnimationSource, workingAnimationTask);
			workingAnimationSource = null;
			workingAnimationTask = null;
			RestoreTransientAiConversationText(originalSource);
			ExecutionStatus = "AI request failed";
			DebugOutputText = exception.ToString();
			WriteInlineAiDebugTrace(DebugOutputText);
			TraceEntries.Clear();
			TraceEntries.Add(new TraceEntryViewModel("ai", exception.Message, DateTime.Now.ToString("T"), _dangerColor));
			OnPropertyChanged(nameof(CanCopyTrace));
			FocusRightPaneTab("debug");
			RevealInspectorOnCompactLayout();
			return true;
		}
		finally
		{
			aiRequestTimeoutSource?.Dispose();
			await StopInlineAiWorkingAnimationAsync(workingAnimationSource, workingAnimationTask);
			IsSending = false;
		}
	}

	private static bool CanStreamInlineAiResponse(
		AiSettings settings,
		AiInlineConversationPrompt? prompt,
		AiInlineConversationResult result)
	{
		return settings.Conversation.StreamResponses &&
		       prompt is not null &&
		       result.Handled &&
		       result.UpdateKind == AiInlineConversationUpdateKind.ResponseOnly &&
		       result.PromptLineNumber == prompt.LineNumber &&
		       !string.IsNullOrWhiteSpace(result.ResponseText);
	}

	private static (string UpdatedText, int? SuggestedCursorLineNumber, int SuggestedCursorColumn) RepairInlineAiConversationResult(
		AiInlineConversationPrompt prompt,
		AiInlineConversationResult result)
	{
		int suggestedCursorColumn = Math.Max(1, result.SuggestedCursorColumn);
		if (IsBlankInlineAiPromptLine(result.UpdatedText, result.SuggestedCursorLineNumber))
		{
			return (result.UpdatedText, result.SuggestedCursorLineNumber, suggestedCursorColumn);
		}

		string repairedText = AiInlineConversationFormatter.EnsureFreshPromptAfterConversation(
			result.UpdatedText,
			result.PromptLineNumber ?? prompt.LineNumber);
		int? suggestedCursorLineNumber = result.SuggestedCursorLineNumber;
		if (!IsBlankInlineAiPromptLine(repairedText, suggestedCursorLineNumber))
		{
			suggestedCursorLineNumber = ResolveFreshInlineAiPromptLineNumber(repairedText, prompt.LineNumber);
			suggestedCursorColumn = 4;
		}

		return (repairedText, suggestedCursorLineNumber, suggestedCursorColumn);
	}

	private static bool IsBlankInlineAiPromptLine(string sourceText, int? lineNumber)
	{
		if (lineNumber is not > 0)
		{
			return false;
		}

		AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
		return document.Prompts.Any(
			prompt => prompt.LineNumber == lineNumber.Value &&
			          string.IsNullOrWhiteSpace(prompt.PromptText));
	}

	private static int? ResolveFreshInlineAiPromptLineNumber(string sourceText, int preferredAfterLineNumber)
	{
		AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
		AiInlineConversationPrompt? prompt = document.Prompts
			.Where(static prompt => string.IsNullOrWhiteSpace(prompt.PromptText))
			.OrderBy(prompt => prompt.LineNumber < preferredAfterLineNumber ? 1 : 0)
			.ThenBy(prompt => Math.Abs(prompt.LineNumber - preferredAfterLineNumber))
			.FirstOrDefault();
		return prompt?.LineNumber;
	}

	private static string BuildInlineAiTimeoutDebugOutput(
		string originalSource,
		AiInlineConversationPrompt? prompt,
		AiSettings aiSettings)
	{
		List<string> lines =
		[
			$"The AI request exceeded the configured {Math.Max(1, aiSettings.Conversation.ExecutionTimeoutSeconds)}-second timeout."
		];

		if (prompt is not null)
		{
			lines.Add($"Resolved inline AI prompt line: {prompt.LineNumber}");
			lines.Add("Resolved inline AI prompt:");
			lines.Add(prompt.PromptText);
		}
		else
		{
			lines.Add("Resolved inline AI prompt: none");
		}

		string attemptedSource = BuildConversationFreeRequestSource(originalSource).TrimEnd();
		if (!string.IsNullOrWhiteSpace(attemptedSource))
		{
			lines.Add("Attempted request source:");
			lines.Add(attemptedSource);
		}

		return string.Join(Environment.NewLine, lines);
	}

	private static string BuildInlineAiTraceSummary(string? promptText)
	{
		string normalized = NormalizeLineEndings(promptText ?? string.Empty)
			.Replace('\n', ' ')
			.Trim();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return "(blank prompt)";
		}

		return normalized.Length <= 72
			? normalized
			: normalized[..72].TrimEnd() + "...";
	}

	private static void WriteInlineAiDebugTrace(string? debugText)
	{
		if (string.IsNullOrWhiteSpace(debugText))
		{
			return;
		}

		System.Diagnostics.Debug.WriteLine($"[InlineAI]{Environment.NewLine}{debugText}");
	}

	private async Task RunInlineAiWorkingAnimationAsync(string sourceText, int promptLineNumber, CancellationToken cancellationToken)
	{
		string[] frames =
		[
			"Working.",
			"Working..",
			"Working..."
		];
		string currentText = sourceText;
		int frameIndex = 0;

		while (!cancellationToken.IsCancellationRequested)
		{
			string nextText = frameIndex == 0
				? AiInlineConversationFormatter.ApplyResponse(sourceText, promptLineNumber, frames[frameIndex])
				: AiInlineConversationFormatter.ReplaceActiveResponse(currentText, promptLineNumber, frames[frameIndex]);
			currentText = nextText;
			await InvokeOnViewModelThreadAsync(() => ApplyTransientAiConversationText(nextText));
			frameIndex = (frameIndex + 1) % frames.Length;
			await Task.Delay(220, cancellationToken);
		}
	}

	private static async Task StopInlineAiWorkingAnimationAsync(CancellationTokenSource? animationSource, Task? animationTask)
	{
		if (animationSource is null)
		{
			return;
		}

		animationSource.Cancel();
		try
		{
			if (animationTask is not null)
			{
				await animationTask;
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			animationSource.Dispose();
		}
	}

	private async Task AnimateInlineAiResponseAsync(int promptLineNumber, string responseText)
	{
		if (string.IsNullOrWhiteSpace(responseText))
		{
			return;
		}

		string normalizedResponse = NormalizeLineEndings(responseText);
		foreach (int breakpoint in BuildInlineAiStreamingBreakpoints(normalizedResponse))
		{
			string partialResponse = normalizedResponse[..breakpoint];
			string updatedText = AiInlineConversationFormatter.ReplaceActiveResponse(
				_requestEditorText,
				promptLineNumber,
				partialResponse);
			await InvokeOnViewModelThreadAsync(() => ApplyTransientAiConversationText(updatedText));
			await Task.Delay(28);
		}
	}

	private static IReadOnlyList<int> BuildInlineAiStreamingBreakpoints(string responseText)
	{
		if (string.IsNullOrEmpty(responseText))
		{
			return [];
		}

		int chunkSize = Math.Clamp(responseText.Length / 18, 3, 24);
		List<int> breakpoints = [];
		for (int index = chunkSize; index < responseText.Length; index += chunkSize)
		{
			breakpoints.Add(index);
		}

		if (breakpoints.Count > 0 && breakpoints[^1] == responseText.Length)
		{
			breakpoints.RemoveAt(breakpoints.Count - 1);
		}

		return breakpoints;
	}

	private void ApplyTransientAiConversationText(string updatedText)
	{
		string normalized = NormalizeLineEndings(updatedText);
		if (string.Equals(_requestEditorText, normalized, StringComparison.Ordinal) &&
		    string.Equals(_activeEditorText, normalized, StringComparison.Ordinal))
		{
			return;
		}

		_requestEditorText = normalized;
		OnPropertyChanged(nameof(RequestEditorText));
		if (IsActiveRequestEditor &&
		    !string.Equals(_activeEditorText, normalized, StringComparison.Ordinal))
		{
			SetActiveEditorTextInternal(normalized);
		}
	}

	private void RestoreTransientAiConversationText(string sourceText)
	{
		ApplyTransientAiConversationText(sourceText);
	}

	private static Task InvokeOnViewModelThreadAsync(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);

		try
		{
			if (MainThread.IsMainThread)
			{
				action();
				return Task.CompletedTask;
			}

			return MainThread.InvokeOnMainThreadAsync(action);
		}
		catch
		{
			action();
			return Task.CompletedTask;
		}
	}

	private void ScheduleRequestAutosave()
	{
		if (_isShuttingDown)
		{
			return;
		}

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
				finally
				{
					if (ReferenceEquals(Volatile.Read(ref _requestSaveSource), saveSource))
					{
						Interlocked.CompareExchange(ref _requestSaveSource, null, saveSource);
					}

					saveSource.Dispose();
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
				BuildConversationFreeRequestSource(_requestEditorText),
				preRequestScript: string.Empty,
				string.IsNullOrWhiteSpace(RequestName) ? "Untitled Request" : RequestName));
		if (!applyToEditor || string.Equals(_requestEditorText, normalized, StringComparison.Ordinal))
		{
			return normalized;
		}

		ApplyRequestDocumentText(
			normalized,
			updateActiveEditor: IsActiveRequestEditor,
			forceActiveEditorRefresh: false,
			recordHistory: true,
			reconcileIdentity: false);
		return normalized;
	}

	private static string BuildConversationFreeRequestSource(string sourceText)
	{
		return NormalizeLineEndings(AiInlineConversationFormatter.RemoveConversationLines(sourceText));
	}

	private static string BuildConversationIgnoredRequestCompilationSource(string sourceText)
	{
		return NormalizeLineEndings(AiInlineConversationFormatter.BlankConversationLines(sourceText));
	}

	private void ApplyRequestDocumentText(
		string updatedText,
		bool updateActiveEditor,
		bool forceActiveEditorRefresh,
		bool recordHistory,
		bool reconcileIdentity,
		bool scheduleAutosave = false,
		string? historyBaselineText = null)
	{
		string previousRequestName = RequestName;
		string previousRequestMethod = SelectedMethod;
		string previousRequestTarget = RequestTarget;
		string previousRequestSummary = RequestSummary;
		string previousRequestLocation = RequestLocation;
		string normalized = NormalizeLineEndings(updatedText);
		string historySourceText = NormalizeLineEndings(historyBaselineText ?? _requestEditorText);

		if (recordHistory)
		{
			RecordDocumentTextChange(
				BuildRequestHistoryKey(_selectedWorkspaceId, previousRequestLocation),
				historySourceText,
				normalized);
		}

		bool requestTextChanged = !string.Equals(_requestEditorText, normalized, StringComparison.Ordinal);
		bool activeEditorTextChanged =
			updateActiveEditor &&
			IsActiveRequestEditor &&
			!string.Equals(_activeEditorText, normalized, StringComparison.Ordinal);

		if (!requestTextChanged && !activeEditorTextChanged)
		{
			if (forceActiveEditorRefresh && IsActiveRequestEditor)
			{
				ForceActiveEditorRefresh();
			}

			return;
		}

		if (requestTextChanged)
		{
			_requestEditorText = normalized;
			OnPropertyChanged(nameof(RequestEditorText));
		}

		if (activeEditorTextChanged)
		{
			SetActiveEditorTextInternal(normalized);
		}

		if (forceActiveEditorRefresh && IsActiveRequestEditor)
		{
			ForceActiveEditorRefresh(includeText: !activeEditorTextChanged);
		}

		if (!requestTextChanged)
		{
			return;
		}

		SyncSupportEditorsFromRequestSource();
		UpdateRequestMetadataFromSource();
		if (reconcileIdentity)
		{
			ReconcileRequestIdentityAfterAiEdit(
				previousRequestName,
				previousRequestMethod,
				previousRequestTarget,
				previousRequestSummary,
				previousRequestLocation);
		}

		MarkCurrentDocumentDirty();
		if (scheduleAutosave && !_suppressRequestAutosave)
		{
			ScheduleRequestAutosave();
		}
	}

	private void ApplyAiConversationText(
		string updatedText,
		int? suggestedCursorLineNumber = null,
		int? suggestedCursorColumn = null,
		string? historyBaselineText = null)
	{
		ApplyRequestDocumentText(
			updatedText,
			updateActiveEditor: IsActiveRequestEditor,
			forceActiveEditorRefresh: IsActiveRequestEditor,
			recordHistory: true,
			reconcileIdentity: true,
			scheduleAutosave: false,
			historyBaselineText: historyBaselineText);

		if (IsActiveRequestEditor && suggestedCursorLineNumber is int lineNumber)
		{
			RequestActiveEditorCursorMove(lineNumber, suggestedCursorColumn ?? 1);
		}
	}

	private void ReconcileRequestIdentityAfterAiEdit(
		string previousRequestName,
		string previousRequestMethod,
		string previousRequestTarget,
		string previousRequestSummary,
		string previousRequestLocation)
	{
		bool identityChanged =
			!string.Equals(previousRequestName, RequestName, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(previousRequestMethod, SelectedMethod, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(previousRequestTarget, RequestTarget, StringComparison.Ordinal);

		if (identityChanged &&
			string.Equals(RequestSummary, previousRequestSummary, StringComparison.Ordinal))
		{
			RequestSummary = BuildGeneratedRequestSummary(SelectedMethod, RequestTarget);
		}

		string rebasedLocation = BuildRebasedRequestLocation(previousRequestLocation, RequestName);
		if (!string.Equals(rebasedLocation, RequestLocation, StringComparison.OrdinalIgnoreCase))
		{
			RebaseActiveRequestLocation(previousRequestLocation, rebasedLocation);
		}
	}

	private void RebaseActiveRequestLocation(string previousLocation, string nextLocation)
	{
		if (string.IsNullOrWhiteSpace(previousLocation) ||
			string.IsNullOrWhiteSpace(nextLocation) ||
			string.Equals(previousLocation, nextLocation, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		RequestWorkbenchWorkspaceState? workspace = GetSelectedWorkspaceState();
		RequestWorkbenchDocumentState currentState = BuildCurrentDocumentState() with
		{
			Location = nextLocation,
		};

		_suppressRequestAutosave = true;
		try
		{
			RequestLocation = nextLocation;
		}
		finally
		{
			_suppressRequestAutosave = false;
		}

		RekeyDocumentHistory(
			BuildRequestHistoryKey(_selectedWorkspaceId, previousLocation),
			BuildRequestHistoryKey(_selectedWorkspaceId, nextLocation));

		if (workspace is null)
		{
			return;
		}

		List<RequestWorkbenchDocumentState> updatedDocuments = [];
		bool replaced = false;
		foreach (RequestWorkbenchDocumentState document in workspace.Documents)
		{
			if (string.Equals(document.Location, previousLocation, StringComparison.OrdinalIgnoreCase))
			{
				updatedDocuments.Add(currentState);
				replaced = true;
				continue;
			}

			updatedDocuments.Add(document);
		}

		if (!replaced)
		{
			updatedDocuments.Add(currentState);
		}

		RequestWorkbenchWorkspaceState updatedWorkspace = workspace with
		{
			SelectedEnvironment = SelectedEnvironment,
			SelectedDocumentLocation = nextLocation,
			Documents = updatedDocuments,
		};
		_workspaceStates[updatedWorkspace.Id] = updatedWorkspace;
		RebuildWorkspaceCollections(updatedWorkspace);
		SelectDocumentByLocation(nextLocation);
		SelectExplorerItemByContext(nextLocation);
	}

	private string BuildRebasedRequestLocation(string currentLocation, string title)
	{
		if (string.IsNullOrWhiteSpace(currentLocation))
		{
			return currentLocation;
		}

		string[] segments = NormalizeExplorerLocation(currentLocation)
			.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (segments.Length == 0)
		{
			return currentLocation;
		}

		string requestSlug = BuildSlug(title, "request");
		string[] candidateSegments = [.. segments.Take(Math.Max(0, segments.Length - 1)), requestSlug];
		string baseLocation = "/" + string.Join('/', candidateSegments);
		RequestWorkbenchWorkspaceState? workspace = GetSelectedWorkspaceState();
		if (workspace is null)
		{
			return baseLocation;
		}

		HashSet<string> existingLocations = workspace.Documents
			.Select(static document => document.Location)
			.Where(static location => !string.IsNullOrWhiteSpace(location))
			.Where(location => !string.Equals(location, currentLocation, StringComparison.OrdinalIgnoreCase))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		if (!existingLocations.Contains(baseLocation))
		{
			return baseLocation;
		}

		int suffix = 2;
		string candidate = $"{baseLocation}-{suffix}";
		while (existingLocations.Contains(candidate))
		{
			suffix++;
			candidate = $"{baseLocation}-{suffix}";
		}

		return candidate;
	}

	private static string BuildGeneratedRequestSummary(string method, string target)
	{
		string normalizedMethod = string.IsNullOrWhiteSpace(method) ? "REQUEST" : method.Trim().ToUpperInvariant();
		if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
		{
			string route = string.IsNullOrWhiteSpace(uri.AbsolutePath) || string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal)
				? uri.Host
				: $"{uri.Host}{uri.AbsolutePath}";
			return $"{normalizedMethod} {route}";
		}

		return $"{normalizedMethod} request";
	}

	private bool TryValidateCompiledRequestScripts(ForRestExecutionPayload payload, out string detail)
	{
		ScriptValidationResult flowValidation = _scriptEngine.Validate(payload.Request.PreRequestScript);
		if (!flowValidation.Succeeded)
		{
			detail = $"The generated flow script does not compile. {NormalizeScriptValidationError(flowValidation.ErrorMessage)}";
			return false;
		}

		ScriptValidationResult testsValidation = _scriptEngine.Validate(payload.Request.TestsScript);
		if (!testsValidation.Succeeded)
		{
			detail = $"The generated tests script does not compile. {NormalizeScriptValidationError(testsValidation.ErrorMessage)}";
			return false;
		}

		detail = string.Empty;
		return true;
	}

	private static string NormalizeScriptValidationError(string errorMessage)
	{
		string[] lines = (errorMessage ?? string.Empty)
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (lines.Length == 0)
		{
			return "The generated script would not compile.";
		}

		return string.Join("  ", lines.Take(4));
	}

	private static IReadOnlyList<AiActiveDocumentDiagnostic> ParseScriptValidationDiagnostics(string detail)
	{
		string[] entries = (detail ?? string.Empty)
			.Split(["\r\n", "\n", "  "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		List<AiActiveDocumentDiagnostic> roslynDiagnostics = [];

		foreach (string entry in entries)
		{
			Match match = Regex.Match(entry, @"\((?<line>\d+),(?<column>\d+)\):\s*error\s+\w+\s*:\s*(?<message>.+)$");
			if (match.Success &&
			    int.TryParse(match.Groups["line"].Value, out int lineNumber) &&
			    int.TryParse(match.Groups["column"].Value, out int columnNumber))
			{
				roslynDiagnostics.Add(new AiActiveDocumentDiagnostic("error", match.Groups["message"].Value.Trim(), lineNumber, columnNumber));
			}
		}

		if (roslynDiagnostics.Count > 0)
		{
			return roslynDiagnostics;
		}

		List<AiActiveDocumentDiagnostic> diagnostics = [];

		foreach (string entry in entries)
		{
			diagnostics.Add(new AiActiveDocumentDiagnostic("error", entry.Trim(), 1, 1));
		}

		return diagnostics;
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
		ApplyRequestSnapshotEntries([]);
		ApplyResponseSnapshotEntries([]);
		DebugOutputText = string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => $"Line {diagnostic.Line}, Col {diagnostic.Column}: {diagnostic.Message}"));
		_responseTimeStatus = "--";
		_responseSizeStatus = "--";
		ExecutionStatus = diagnostics.Count == 0
			? "Request document failed to compile"
			: diagnostics[0].Message;
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
		if (_isShuttingDown || !_isInitialized || !IsActiveRequestEditor || string.IsNullOrWhiteSpace(RequestLocation))
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

	private void ForceActiveEditorRefresh(bool includeText = true)
	{
		OnPropertyChanged(nameof(ActiveEditorLanguage));
		OnPropertyChanged(nameof(ActiveEditorEditableRangesJson));
		if (includeText)
		{
			OnPropertyChanged(nameof(ActiveEditorText));
		}
	}

	private void RequestActiveEditorCursorMove(int lineNumber, int column)
	{
		ActiveEditorRequestedCursorLineNumber = Math.Max(1, lineNumber);
		ActiveEditorRequestedCursorColumn = Math.Max(1, column);
		ActiveEditorRequestedCursorVersion++;
	}

	private void ScheduleSettingsAutosave()
	{
		if (_isShuttingDown)
		{
			return;
		}

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
				if (!saveSource.IsCancellationRequested)
				{
					RefreshSettingsEditorAfterSave();
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				if (ReferenceEquals(Volatile.Read(ref _settingsSaveSource), saveSource))
				{
					Interlocked.CompareExchange(ref _settingsSaveSource, null, saveSource);
				}

				saveSource.Dispose();
			}
		});
	}

	private void RefreshSettingsEditorAfterSave()
	{
		void refresh()
		{
			if (_isShuttingDown)
			{
				return;
			}

			RequestSettingsProjectionRefresh(_currentThemeName, _latestActivationSnapshot);
		}

		try
		{
			if (MainThread.IsMainThread)
			{
				refresh();
			}
			else
			{
				MainThread.BeginInvokeOnMainThread(refresh);
			}
		}
		catch
		{
			refresh();
		}
	}

	private void RequestSettingsProjectionRefresh(ShellThemeName currentTheme, ActivationSnapshot activation)
	{
		if (_isShuttingDown)
		{
			return;
		}

		if (!IsActiveSettingsEditor)
		{
			CancelPendingSettingsProjectionRefresh();
			UpdateSettingsTextFromDisk(currentTheme, activation);
			return;
		}

		TimeSpan elapsedSinceEdit = DateTimeOffset.UtcNow - _lastSettingsEditUtc;
		if (_lastSettingsEditUtc == DateTimeOffset.MinValue || elapsedSinceEdit >= SettingsEditorProjectionRefreshQuietPeriod)
		{
			CancelPendingSettingsProjectionRefresh();
			UpdateSettingsTextFromDisk(currentTheme, activation);
			return;
		}

		TimeSpan delay = SettingsEditorProjectionRefreshQuietPeriod - elapsedSinceEdit;
		CancellationTokenSource refreshSource = new();
		CancellationTokenSource? previousSource = Interlocked.Exchange(ref _settingsProjectionRefreshSource, refreshSource);
		previousSource?.Cancel();
		previousSource?.Dispose();

		_ = Task.Run(
			async () =>
			{
				try
				{
					await Task.Delay(delay, refreshSource.Token);
					await InvokeOnViewModelThreadAsync(
						() =>
						{
							if (refreshSource.IsCancellationRequested || _isShuttingDown)
							{
								return;
							}

							UpdateSettingsTextFromDisk(currentTheme, activation);
						});
				}
				catch (OperationCanceledException)
				{
				}
				finally
				{
					if (ReferenceEquals(Volatile.Read(ref _settingsProjectionRefreshSource), refreshSource))
					{
						Interlocked.CompareExchange(ref _settingsProjectionRefreshSource, null, refreshSource);
					}

					refreshSource.Dispose();
				}
			});
	}

	private void UpdateSettingsTextFromDisk(ShellThemeName currentTheme, ActivationSnapshot activation)
	{
		string latestText = ReadSettingsText(currentTheme, activation);
		_themeConfigText = latestText;

		if (!IsActiveSettingsEditor)
		{
			return;
		}

		_suppressSettingsAutosave = true;
		try
		{
			ActiveEditorEditableRangesJson = BuildEditableRangesJson(latestText);
			if (!string.Equals(_activeEditorText, latestText, StringComparison.Ordinal))
			{
				SetActiveEditorTextInternal(latestText);
			}
		}
		finally
		{
			_suppressSettingsAutosave = false;
		}
	}

	private string ReadSettingsText(ShellThemeName currentTheme, ActivationSnapshot activation)
	{
		try
		{
			return NormalizeLineEndings(_settingsTomlDocumentService.LoadOrCreate(_themeService.CurrentSettings with { Theme = currentTheme }, activation));
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Settings text load failed.", exception);
			return string.Empty;
		}
	}

	private static ActivationSnapshot CreatePendingActivationSnapshot()
	{
		return new(
			LicenseAccessStatus.Pending,
			"Activation pending",
			"License state has not been evaluated yet.",
			true);
	}

	private static ActivationSnapshot CreateUnavailableActivationSnapshot(string detail)
	{
		return new(
			LicenseAccessStatus.Pending,
			"Activation unavailable",
			detail,
			true);
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
		double restoreRails = GetCollapsedRestoreRailPixels();
		double max = Math.Max(MinLeftPanePixels, totalWidth - right - splitters - restoreRails - MinCenterPanePixels);
		return Math.Clamp(requestedWidth, MinLeftPanePixels, max);
	}

	private double ClampRightPane(double requestedWidth, double totalWidth)
	{
		double left = _leftPaneCollapsed ? 0d : _leftPanePixels;
		double splitters = (_leftPaneCollapsed ? 0d : SplitterPixels) + (_rightPaneCollapsed ? 0d : SplitterPixels);
		double restoreRails = GetCollapsedRestoreRailPixels();
		double max = Math.Max(MinRightPanePixels, totalWidth - left - splitters - restoreRails - MinCenterPanePixels);
		return Math.Clamp(requestedWidth, MinRightPanePixels, max);
	}

	private double GetCollapsedRestoreRailPixels()
	{
		if (_isCompactLayout)
		{
			return 0d;
		}

		double leftRail = _leftPaneCollapsed ? CollapsedPaneRailPixels : 0d;
		double rightRail = _rightPaneCollapsed ? CollapsedPaneRailPixels : 0d;
		return leftRail + rightRail;
	}

	private void NotifyPaneLayoutChanged()
	{
		OnPropertyChanged(nameof(IsLeftPaneVisible));
		OnPropertyChanged(nameof(IsRightPaneVisible));
		OnPropertyChanged(nameof(LeftRestoreRailWidth));
		OnPropertyChanged(nameof(RightRestoreRailWidth));
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
		ResponseBodyText = ResponsePresentationFormatter.FormatBody(_selectedInspectorResponseSnapshot?.Body, IsResponsePrettyPrintEnabled);
		ResponseRawText = ResponsePresentationFormatter.NormalizeDisplayText(_selectedInspectorResponseSnapshot?.RawResponse);
	}

	private void RefreshRequestPresentation()
	{
		RequestBodyText = ResponsePresentationFormatter.FormatBody(_selectedInspectorRequestSnapshot?.Body, IsResponsePrettyPrintEnabled);
		RequestRawText = ResponsePresentationFormatter.NormalizeDisplayText(_selectedInspectorRequestSnapshot?.RawRequest);
	}

	private void ApplySelectedResponseSnapshot(ResponseSnapshot? response)
	{
		_selectedInspectorResponseSnapshot = response;
		RefreshResponsePresentation();
		ResponseHeaderRows.Clear();
		foreach (KeyValueDefinition header in response?.Headers ?? [])
		{
			ResponseHeaderRows.Add(new NameValueRowViewModel(header.Key, header.Value, "response"));
		}

		OnPropertyChanged(nameof(CanCopyHeaders));
	}

	private void ApplySelectedRequestSnapshot(RequestSnapshot? request)
	{
		_selectedInspectorRequestSnapshot = request;
		RefreshRequestPresentation();
	}

	private void ApplyResponseSnapshotEntries(IReadOnlyList<ResponseSnapshot> responses)
	{
		SelectedResponseSnapshotEntry = null;
		ResponseSnapshotEntries.Clear();

		for (int index = responses.Count - 1; index >= 0; index--)
		{
			ResponseSnapshot response = responses[index];
			ResponseSnapshotEntries.Add(
				new ResponseSnapshotEntryViewModel(
					response,
					index + 1,
					$"{response.StatusCode} {response.ReasonPhrase}".Trim(),
					$"{response.DurationMilliseconds} ms  {FormatResponseSize(response.SizeBytes)}",
					ResolveResponseSnapshotAccent(response)));
		}

		OnPropertyChanged(nameof(ShowResponseSnapshotSelector));
		OnPropertyChanged(nameof(CanSelectPreviousResponseSnapshot));
		OnPropertyChanged(nameof(CanSelectNextResponseSnapshot));
		if (ResponseSnapshotEntries.Count == 0)
		{
			OnPropertyChanged(nameof(SelectedResponseSnapshotSummaryText));
			return;
		}

		SelectedResponseSnapshotEntry = ResponseSnapshotEntries[0];
	}

	private void ApplyRequestSnapshotEntries(IReadOnlyList<RequestSnapshot> requests)
	{
		SelectedRequestSnapshotEntry = null;
		RequestSnapshotEntries.Clear();

		for (int index = requests.Count - 1; index >= 0; index--)
		{
			RequestSnapshot request = requests[index];
			RequestSnapshotEntries.Add(
				new RequestSnapshotEntryViewModel(
					request,
					index + 1,
					request.Method,
					BuildRequestSnapshotCardDetail(request),
					ResolveMethodAccent(request.Method)));
		}

		OnPropertyChanged(nameof(ShowRequestSnapshotSelector));
		OnPropertyChanged(nameof(CanSelectPreviousRequestSnapshot));
		OnPropertyChanged(nameof(CanSelectNextRequestSnapshot));
		if (RequestSnapshotEntries.Count == 0)
		{
			OnPropertyChanged(nameof(SelectedRequestSnapshotSummaryText));
			OnPropertyChanged(nameof(SelectedRequestSnapshotTargetText));
			OnPropertyChanged(nameof(SelectedRequestSnapshotMetaText));
			return;
		}

		SelectedRequestSnapshotEntry = RequestSnapshotEntries[0];
	}

	public bool SelectPreviousResponseSnapshot()
	{
		int selectedIndex = GetSelectedResponseSnapshotEntryIndex();
		if (selectedIndex <= 0)
		{
			return false;
		}

		SelectedResponseSnapshotEntry = ResponseSnapshotEntries[selectedIndex - 1];
		return true;
	}

	public bool SelectNextResponseSnapshot()
	{
		int selectedIndex = GetSelectedResponseSnapshotEntryIndex();
		if (selectedIndex < 0 || selectedIndex >= ResponseSnapshotEntries.Count - 1)
		{
			return false;
		}

		SelectedResponseSnapshotEntry = ResponseSnapshotEntries[selectedIndex + 1];
		return true;
	}

	public bool SelectPreviousRequestSnapshot()
	{
		int selectedIndex = GetSelectedRequestSnapshotEntryIndex();
		if (selectedIndex <= 0)
		{
			return false;
		}

		SelectedRequestSnapshotEntry = RequestSnapshotEntries[selectedIndex - 1];
		return true;
	}

	public bool SelectNextRequestSnapshot()
	{
		int selectedIndex = GetSelectedRequestSnapshotEntryIndex();
		if (selectedIndex < 0 || selectedIndex >= RequestSnapshotEntries.Count - 1)
		{
			return false;
		}

		SelectedRequestSnapshotEntry = RequestSnapshotEntries[selectedIndex + 1];
		return true;
	}

	private int GetSelectedResponseSnapshotEntryIndex()
	{
		return SelectedResponseSnapshotEntry is null
			? -1
			: ResponseSnapshotEntries.IndexOf(SelectedResponseSnapshotEntry);
	}

	private int GetSelectedRequestSnapshotEntryIndex()
	{
		return SelectedRequestSnapshotEntry is null
			? -1
			: RequestSnapshotEntries.IndexOf(SelectedRequestSnapshotEntry);
	}

	private void SynchronizeSelectedRequestSnapshot(int? position)
	{
		if (_isSynchronizingInspectorSnapshotSelection)
		{
			return;
		}

		_isSynchronizingInspectorSnapshotSelection = true;
		try
		{
			SelectedRequestSnapshotEntry = position is int value
				? RequestSnapshotEntries.FirstOrDefault(item => item.Position == value)
				: null;
		}
		finally
		{
			_isSynchronizingInspectorSnapshotSelection = false;
		}
	}

	private void SynchronizeSelectedResponseSnapshot(int? position)
	{
		if (_isSynchronizingInspectorSnapshotSelection)
		{
			return;
		}

		_isSynchronizingInspectorSnapshotSelection = true;
		try
		{
			SelectedResponseSnapshotEntry = position is int value
				? ResponseSnapshotEntries.FirstOrDefault(item => item.Position == value)
				: null;
		}
		finally
		{
			_isSynchronizingInspectorSnapshotSelection = false;
		}
	}

	private RequestSnapshot? ResolveRequestSnapshotForSelectedResponse()
	{
		if (SelectedResponseSnapshotEntry is null)
		{
			return null;
		}

		if (SelectedRequestSnapshotEntry is not null &&
		    SelectedRequestSnapshotEntry.Position == SelectedResponseSnapshotEntry.Position)
		{
			return SelectedRequestSnapshotEntry.Snapshot;
		}

		return RequestSnapshotEntries
			.FirstOrDefault(item => item.Position == SelectedResponseSnapshotEntry.Position)
			?.Snapshot;
	}

	private Color ResolveResponseSnapshotAccent(ResponseSnapshot response)
	{
		return response.StatusCode switch
		{
			>= 200 and < 300 => _successColor,
			>= 300 and < 400 => _warningColor,
			>= 400 => _dangerColor,
			_ => _methodNeutral,
		};
	}

	private static List<ResponseSnapshot> BuildCapturedResponses(ExecutionRun? run, ResponseSnapshot? fallbackResponse = null)
	{
		if (run?.Responses is { Count: > 0 } capturedResponses)
		{
			return [.. capturedResponses];
		}

		if (run?.Response is { } runResponse)
		{
			return [runResponse];
		}

		return fallbackResponse is null ? [] : [fallbackResponse];
	}

	private static List<RequestSnapshot> BuildCapturedRequests(ExecutionRun? run, int responseCount)
	{
		if (run?.Requests is { Count: > 0 } capturedRequests)
		{
			if (responseCount <= 1 || capturedRequests.Count == responseCount)
			{
				return [.. capturedRequests];
			}

			return [];
		}

		if (run is null || string.IsNullOrWhiteSpace(run.RawRequest))
		{
			return [];
		}

		if (responseCount > 1)
		{
			return [];
		}

		return [BuildFallbackRequestSnapshot(run)];
	}

	private static RequestSnapshot BuildFallbackRequestSnapshot(ExecutionRun run)
	{
		string normalizedRawRequest = ResponsePresentationFormatter.NormalizeDisplayText(run.RawRequest);
		string[] lines = normalizedRawRequest.Split('\n');
		string firstLine = lines.FirstOrDefault() ?? string.Empty;
		string[] firstLineParts = firstLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
		string method = firstLineParts.Length > 0 ? firstLineParts[0] : ResolveHistoryMethod(run);
		string url = firstLineParts.Length > 1 ? firstLineParts[1] : run.TargetUri;
		List<KeyValueDefinition> headers = [];
		List<string> bodyLines = [];
		bool bodyStarted = false;

		for (int index = 1; index < lines.Length; index++)
		{
			string line = lines[index];
			if (!bodyStarted)
			{
				if (string.IsNullOrWhiteSpace(line))
				{
					bodyStarted = true;
					continue;
				}

				int separatorIndex = line.IndexOf(':');
				if (separatorIndex > 0)
				{
					headers.Add(
						new KeyValueDefinition
						{
							Key = line[..separatorIndex].Trim(),
							Value = line[(separatorIndex + 1)..].Trim(),
						});
				}

				continue;
			}

			bodyLines.Add(line);
		}

		string body = string.Join('\n', bodyLines).TrimEnd();
		string contentType = headers
			.FirstOrDefault(static header => string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
			?.Value
			?? string.Empty;

		return new RequestSnapshot
		{
			Method = method,
			Url = string.IsNullOrWhiteSpace(url) ? run.TargetUri : url,
			ContentType = contentType,
			SizeBytes = Encoding.UTF8.GetByteCount(body),
			Body = body,
			RawRequest = normalizedRawRequest,
			Headers = headers,
			SentUtc = run.StartedUtc,
		};
	}

	private static string BuildRequestSnapshotCardDetail(RequestSnapshot request)
	{
		if (Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? uri))
		{
			string target = string.IsNullOrWhiteSpace(uri.PathAndQuery) ? uri.Host : $"{uri.Host}{uri.PathAndQuery}";
			return string.IsNullOrWhiteSpace(target) ? request.Url : target;
		}

		return request.Url;
	}

	private ForRestEditorDebugSnapshot CreateRequestEditorDebugSnapshot(ForRestScriptCompilationResult compilation, string source)
	{
		return ForRestEditorDebugSnapshotFactory.Create(
			source,
			compilation,
			SelectedWorkspace,
			SelectedEnvironment,
			string.IsNullOrWhiteSpace(RequestName) ? "Untitled Request" : RequestName,
			BuildDefaultRequestUrl(RequestLocation));
	}

	private void ApplyEditorDebugSnapshot(ForRestEditorDebugSnapshot snapshot)
	{
		ApplyEditorDebugSnapshot(snapshot, updateDebugOutput: true);
	}

	private void ApplyEditorDebugSnapshot(ForRestEditorDebugSnapshot snapshot, bool updateDebugOutput)
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
		if (updateDebugOutput)
		{
			DebugOutputText = snapshot.DebugOutputText;
		}
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
		_responseTimeStatus = response is not null ? $"{response.DurationMilliseconds} ms" : "--";
		_responseSizeStatus = response is not null ? FormatResponseSize(response.SizeBytes) : "--";
		List<ResponseSnapshot> capturedResponses = BuildCapturedResponses(run, response);
		ApplyRequestSnapshotEntries(BuildCapturedRequests(run, capturedResponses.Count));
		ApplyResponseSnapshotEntries(capturedResponses);
		DebugOutputText = BuildDebugOutput(run, SelectedWorkspace, SelectedEnvironment, method);
		RecordLatestRuntimeContext(run.State.ToString(), run.ErrorMessage, DebugOutputText);
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

	private void RecordLatestRuntimeContext(string? statusText, string? errorText, string? debugText)
	{
		_latestRuntimeStateText = statusText ?? string.Empty;
		_latestRuntimeErrorText = errorText ?? string.Empty;
		_latestRuntimeDebugText = debugText ?? string.Empty;
	}

	private void ClearLatestRuntimeContext()
	{
		_latestRuntimeStateText = string.Empty;
		_latestRuntimeErrorText = string.Empty;
		_latestRuntimeDebugText = string.Empty;
	}

	private AiActiveDocumentRuntimeContext? BuildLatestRuntimeContext()
	{
		string debugText = string.IsNullOrWhiteSpace(_latestRuntimeDebugText) && LooksLikeRuntimeDebugOutput(DebugOutputText)
			? DebugOutputText
			: _latestRuntimeDebugText;
		string statusText = string.IsNullOrWhiteSpace(_latestRuntimeStateText)
			? ExtractRuntimeDebugLine(debugText, "Execution state:")
				?? (LooksLikeRuntimeDebugOutput(DebugOutputText) ? ExtractRuntimeDebugLine(DebugOutputText, "Execution state:") : string.Empty)
				?? string.Empty
			: _latestRuntimeStateText;
		string errorText = string.IsNullOrWhiteSpace(_latestRuntimeErrorText)
			? ExtractRuntimeDebugLine(debugText, "Error:")
				?? (LooksLikeRuntimeDebugOutput(DebugOutputText) ? ExtractRuntimeDebugLine(DebugOutputText, "Error:") : string.Empty)
				?? string.Empty
			: _latestRuntimeErrorText;
		string responsePreview = BuildRuntimeResponsePreview(_latestResponseSnapshot?.Body);
		if (string.IsNullOrWhiteSpace(statusText) &&
			string.IsNullOrWhiteSpace(errorText) &&
			string.IsNullOrWhiteSpace(debugText) &&
			string.IsNullOrWhiteSpace(responsePreview))
		{
			return null;
		}

		return new(
			Status: statusText,
			ErrorMessage: errorText,
			DebugText: debugText,
			ResponseBodyPreview: responsePreview);
	}

	private static string BuildRuntimeResponsePreview(string? value)
	{
		string normalized = (value ?? string.Empty).Trim();
		return normalized.Length <= 400
			? normalized
			: normalized[..400].TrimEnd() + " ...";
	}

	private static bool LooksLikeRuntimeDebugOutput(string? value)
	{
		string normalized = value ?? string.Empty;
		return normalized.Contains("Execution state:", StringComparison.Ordinal) ||
			normalized.Contains("Response:", StringComparison.Ordinal) ||
			normalized.Contains("Error:", StringComparison.Ordinal) ||
			normalized.Contains("Console:", StringComparison.Ordinal);
	}

	private static string? ExtractRuntimeDebugLine(string? value, string prefix)
	{
		foreach (string line in (value ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
		{
			if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return line[prefix.Length..].Trim();
			}
		}

		return null;
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

	private sealed class DocumentTextHistory
	{
		private readonly int _capacity;
		private readonly List<string> _undoStates = [];
		private readonly List<string> _redoStates = [];

		public DocumentTextHistory(int capacity)
		{
			_capacity = Math.Max(1, capacity);
		}

		public bool CanUndo => _undoStates.Count > 0;

		public bool CanRedo => _redoStates.Count > 0;

		public void RecordChange(string previousText, string currentText)
		{
			if (string.Equals(previousText, currentText, StringComparison.Ordinal))
			{
				return;
			}

			PushState(_undoStates, previousText);
			_redoStates.Clear();
		}

		public bool TryUndo(string currentText, out string previousText)
		{
			previousText = string.Empty;
			while (_undoStates.Count > 0)
			{
				string candidate = PopState(_undoStates);
				if (string.Equals(candidate, currentText, StringComparison.Ordinal))
				{
					continue;
				}

				PushState(_redoStates, currentText);
				previousText = candidate;
				return true;
			}

			return false;
		}

		public bool TryRedo(string currentText, out string nextText)
		{
			nextText = string.Empty;
			while (_redoStates.Count > 0)
			{
				string candidate = PopState(_redoStates);
				if (string.Equals(candidate, currentText, StringComparison.Ordinal))
				{
					continue;
				}

				PushState(_undoStates, currentText);
				nextText = candidate;
				return true;
			}

			return false;
		}

		private void PushState(List<string> states, string text)
		{
			if (states.Count > 0 &&
			    string.Equals(states[^1], text, StringComparison.Ordinal))
			{
				return;
			}

			states.Add(text);
			if (states.Count > _capacity)
			{
				states.RemoveAt(0);
			}
		}

		private static string PopState(List<string> states)
		{
			string value = states[^1];
			states.RemoveAt(states.Count - 1);
			return value;
		}
	}

	private sealed class ActiveRequestDocumentHost : IAiActiveDocumentHost
	{
		private readonly MainPageViewModel _owner;
		private string _sourceText;
		private DiagnosticsCacheEntry? _diagnosticsCache;

		private sealed record DiagnosticsCacheEntry(
			string SourceText,
			Guid WorkspaceId,
			string RequestName,
			IReadOnlyList<AiActiveDocumentDiagnostic> Diagnostics);

		public ActiveRequestDocumentHost(MainPageViewModel owner, string sourceText)
		{
			_owner = owner;
			_sourceText = MainPageViewModel.BuildConversationFreeRequestSource(sourceText);
		}

		public AiActiveDocumentSnapshot? GetActiveDocument()
		{
			if (!_owner.IsActiveRequestEditor || string.IsNullOrWhiteSpace(_owner.RequestLocation))
			{
				return null;
			}

			return new(
				DocumentId: _owner.RequestLocation,
				Title: _owner.RequestName,
				Language: _owner.ActiveEditorLanguage,
				SourceText: _sourceText,
				Diagnostics: GetDiagnostics(_sourceText),
				RuntimeContext: _owner.BuildLatestRuntimeContext());
		}

		public AiActiveDocumentUpdateResult UpdateActiveDocument(AiActiveDocumentSnapshot document, string updatedText)
		{
			WriteUpdateDebug(
				"Requested replacement source",
				string.Join(
					Environment.NewLine,
					[
						$"DocumentId: {document.DocumentId}",
						$"Title: {document.Title}",
						"Source:",
						updatedText ?? string.Empty,
					]));
			if (!_owner.IsActiveRequestEditor)
			{
				WriteUpdateDebug("Rejected replacement", "The active editor is not a request document.");
				return AiActiveDocumentUpdateResult.Failure("The active editor is not a request document.");
			}

			if (!string.Equals(_owner.RequestLocation, document.DocumentId, StringComparison.OrdinalIgnoreCase))
			{
				WriteUpdateDebug("Rejected replacement", "The active request changed before the AI patch could be applied.");
				return AiActiveDocumentUpdateResult.Failure("The active request changed before the AI patch could be applied.");
			}

			string normalizedText = NormalizeLineEndings(
				RequestWorkbenchDocumentNormalizer.NormalizeRequestDocumentSource(
					MainPageViewModel.BuildConversationFreeRequestSource(updatedText ?? string.Empty),
					preRequestScript: string.Empty,
					string.IsNullOrWhiteSpace(_owner.RequestName) ? "Untitled Request" : _owner.RequestName));
			if (!string.Equals(updatedText ?? string.Empty, normalizedText, StringComparison.Ordinal))
			{
				WriteUpdateDebug("Normalized replacement source", normalizedText);
			}

			ForRestScriptCompilationResult compilation = _owner._scriptExecutionService.Compile(
				normalizedText,
				_owner._selectedWorkspaceId,
				_owner.RequestName);
			IReadOnlyList<AiActiveDocumentDiagnostic>? diagnostics = null;
			if (!compilation.Succeeded || compilation.Payload is null)
			{
				string detail = compilation.Diagnostics.Count == 0
					? "The AI edit introduced a request syntax error."
					: string.Join(
						"  ",
						compilation.Diagnostics.Take(3).Select(static diagnostic => $"L{diagnostic.Line}: {diagnostic.Message}"));
				WriteUpdateDebug("Rejected replacement during compile", detail);
				diagnostics = BuildDiagnosticsFromCompilation(compilation, scriptValidationDetail: null);
				return AiActiveDocumentUpdateResult.Failure(
					$"The AI edit was rejected because it left the request invalid. {detail} Read the active document again and use the current diagnostics to repair it.",
					normalizedText,
					diagnostics,
					retryWithReplace: true);
			}

			if (!_owner.TryValidateCompiledRequestScripts(compilation.Payload, out string scriptValidationDetail))
			{
				WriteUpdateDebug("Rejected replacement during script validation", scriptValidationDetail);
				string repairHint = scriptValidationDetail.Contains("expect", StringComparison.OrdinalIgnoreCase)
					? "Read the active document again and use the exact `expect` syntax from the local docs."
					: "Read the active document again and use the current diagnostics plus local docs to repair the flow script.";
				diagnostics = BuildDiagnosticsFromCompilation(compilation, scriptValidationDetail);
				return AiActiveDocumentUpdateResult.Failure(
					$"The AI edit was rejected because its generated scripts do not compile. {scriptValidationDetail} {repairHint}",
					normalizedText,
					diagnostics,
					retryWithReplace: true);
			}

			void apply()
			{
				_owner.ApplyAiConversationText(normalizedText);
			}

			try
			{
				MainPageViewModel.InvokeOnViewModelThreadAsync(apply).GetAwaiter().GetResult();
			}
			catch (Exception exception)
			{
				WriteUpdateDebug("Rejected replacement during apply", exception.ToString());
				return AiActiveDocumentUpdateResult.Failure(exception.Message, normalizedText);
			}

			_sourceText = normalizedText;
			CacheDiagnostics(
				_sourceText,
				_owner._selectedWorkspaceId,
				_owner.RequestName,
				BuildDiagnosticsFromCompilation(compilation, string.Empty));
			WriteUpdateDebug("Applied replacement source", normalizedText);
			return AiActiveDocumentUpdateResult.Success(normalizedText);
		}

		public AiWorkspaceContext? GetWorkspaceContext()
		{
			RequestWorkbenchWorkspaceState? workspace = _owner.GetSelectedWorkspaceState();
			if (workspace is null)
			{
				return null;
			}

			List<AiWorkspaceScriptSummary> scripts = workspace.Documents
				.Select(static document => new AiWorkspaceScriptSummary(
					document.Location,
					document.Title,
					document.Method,
					document.Summary))
				.ToList();

			return new AiWorkspaceContext(
				workspace.Id.ToString(),
				workspace.Name,
				scripts);
		}

		public AiActiveDocumentUpdateResult CreateScript(string name, string sourceText)
		{
			return AiActiveDocumentUpdateResult.Success();
		}

		private static void WriteUpdateDebug(string title, string detail)
		{
			System.Diagnostics.Debug.WriteLine(
				string.IsNullOrWhiteSpace(detail)
					? $"[InlineAI.Update]{Environment.NewLine}{title}"
					: $"[InlineAI.Update]{Environment.NewLine}{title}{Environment.NewLine}{detail}");
		}

		private IReadOnlyList<AiActiveDocumentDiagnostic> GetDiagnostics(string sourceText)
		{
			Guid workspaceId = _owner._selectedWorkspaceId;
			string requestName = _owner.RequestName;
			if (TryGetCachedDiagnostics(sourceText, workspaceId, requestName, out IReadOnlyList<AiActiveDocumentDiagnostic>? diagnostics))
			{
				return diagnostics;
			}

			ForRestScriptCompilationResult compilation = _owner._scriptExecutionService.Compile(
				sourceText,
				workspaceId,
				requestName);
			return CacheDiagnostics(
				sourceText,
				workspaceId,
				requestName,
				BuildDiagnosticsFromCompilation(compilation, scriptValidationDetail: null));
		}

		private IReadOnlyList<AiActiveDocumentDiagnostic> BuildDiagnosticsFromCompilation(
			ForRestScriptCompilationResult compilation,
			string? scriptValidationDetail)
		{
			try
			{
				List<AiActiveDocumentDiagnostic> diagnostics =
				[
					.. compilation.Diagnostics
						.OrderByDescending(static diagnostic => diagnostic.Severity == ForRestScriptDiagnosticSeverity.Error)
						.ThenBy(static diagnostic => diagnostic.Line)
						.ThenBy(static diagnostic => diagnostic.Column)
						.Select(
							static diagnostic => new AiActiveDocumentDiagnostic(
								diagnostic.Severity == ForRestScriptDiagnosticSeverity.Error ? "error" : "warning",
								diagnostic.Message,
								Math.Max(1, diagnostic.Line),
								Math.Max(1, diagnostic.Column)))
				];

				if (scriptValidationDetail is null &&
				    compilation.Succeeded &&
				    compilation.Payload is not null &&
				    !_owner.TryValidateCompiledRequestScripts(compilation.Payload, out string computedScriptValidationDetail))
				{
					diagnostics.AddRange(ParseScriptValidationDiagnostics(computedScriptValidationDetail));
				}
				else if (!string.IsNullOrWhiteSpace(scriptValidationDetail))
				{
					diagnostics.AddRange(ParseScriptValidationDiagnostics(scriptValidationDetail));
				}

				return
				[
					.. diagnostics
						.OrderByDescending(static diagnostic => string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase))
						.ThenBy(static diagnostic => diagnostic.Line)
						.ThenBy(static diagnostic => diagnostic.Column)
						.Take(12)
				];
			}
			catch (Exception exception)
			{
				return
				[
					new(
						"error",
						$"Unable to compile the active document: {exception.Message}",
						1,
						1)
				];
			}
		}

		private IReadOnlyList<AiActiveDocumentDiagnostic> CacheDiagnostics(
			string sourceText,
			Guid workspaceId,
			string requestName,
			IReadOnlyList<AiActiveDocumentDiagnostic> diagnostics)
		{
			_diagnosticsCache = new(sourceText, workspaceId, requestName, diagnostics);
			return diagnostics;
		}

		private bool TryGetCachedDiagnostics(
			string sourceText,
			Guid workspaceId,
			string requestName,
			out IReadOnlyList<AiActiveDocumentDiagnostic> diagnostics)
		{
			if (_diagnosticsCache is not null &&
			    _diagnosticsCache.WorkspaceId == workspaceId &&
			    string.Equals(_diagnosticsCache.SourceText, sourceText, StringComparison.Ordinal) &&
			    string.Equals(_diagnosticsCache.RequestName, requestName, StringComparison.Ordinal))
			{
				diagnostics = _diagnosticsCache.Diagnostics;
				return true;
			}

			diagnostics = Array.Empty<AiActiveDocumentDiagnostic>();
			return false;
		}
	}
}
