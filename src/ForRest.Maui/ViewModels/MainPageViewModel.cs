using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ForRest.Maui.ViewModels;

public sealed class MainPageViewModel : ObservableObject
{
	private const double DefaultLeftPanePixels = 276d;
	private const double DefaultRightPanePixels = 344d;
	private const double MinLeftPanePixels = 228d;
	private const double MinRightPanePixels = 260d;
	private const double MinCenterPanePixels = 560d;
	private const double SplitterPixels = 8d;
	private const double CompactLayoutBreakpoint = 980d;
	private const double CompactPaneMinWidth = 300d;
	private const double CompactPaneMaxWidth = 440d;

	private readonly Color _methodGet = Color.FromArgb("#167C65");
	private readonly Color _methodPost = Color.FromArgb("#176AB8");
	private readonly Color _methodPut = Color.FromArgb("#9A5A1A");
	private readonly Color _methodDelete = Color.FromArgb("#B2433D");
	private readonly Color _methodNeutral = Color.FromArgb("#5D6978");

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

	public MainPageViewModel()
	{
		_selectedWorkspace = "for-rest://echo-lab";
		_selectedEnvironment = "Local";
		_selectedMethod = "POST";
		_requestName = "Echo POST";
		_requestSummary = "Echo POST";
		_requestLocation = "/requests/echo/post";
		_requestTarget = BuildRequestTarget(_requestLocation);
		_requestEditorText = BuildRequestEditorText(_requestName, _selectedMethod, _requestTarget);
		_headersEditorText = BuildHeadersEditorText();
		_bodyEditorText = BuildBodyEditorText(_requestName);
		_scriptEditorText = BuildScriptEditorText(_requestName);
		_testsEditorText = BuildTestsEditorText();
		_variablesEditorText = BuildVariablesEditorText();
		_responseBodyText = BuildResponseBodyText();
		_responseRawText = BuildResponseRawText();
		_responseState = "200 OK";

		LeftPaneTabs =
		[
			new PaneTabViewModel("explorer", "Explorer", true),
			new PaneTabViewModel("history", "History")
		];

		OpenDocuments =
		[
			new RequestDocumentViewModel("Echo POST", "POST", "Current request draft", "/requests/echo/post", true, true),
			new RequestDocumentViewModel("Users Feed", "GET", "Read-only collection fetch", "/requests/users/list", false),
			new RequestDocumentViewModel("Sync Profile", "PUT", "Mutation workflow placeholder", "/requests/users/sync", false)
		];

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
			new PaneTabViewModel("raw", "Raw")
		];

		ExplorerSections =
		[
			new NavigationSectionViewModel(
				"Workspace",
				[
					new NavigationItemViewModel("WK", "workspace.forrest", "Workspace manifest and pane state", "~/echo-lab", _methodNeutral),
					new NavigationItemViewModel("ENV", "env.local", "Local variables and secrets", "~/environments", _methodNeutral, depth: 1),
					new NavigationItemViewModel("SCR", "common.frs", "Shared request helpers", "~/scripts", _methodNeutral, depth: 1)
				]),
			new NavigationSectionViewModel(
				"Requests",
				[
					new NavigationItemViewModel("POST", "Echo POST", "Primary shell draft", "/requests/echo/post", _methodPost, "POST", isSelected: true),
					new NavigationItemViewModel("GET", "Users Feed", "Read-heavy collection request", "/requests/users/list", _methodGet, "GET"),
					new NavigationItemViewModel("PUT", "Sync Profile", "Mutation draft with script hooks", "/requests/users/sync", _methodPut, "PUT"),
					new NavigationItemViewModel("DEL", "Delete Session", "Danger flow placeholder", "/requests/session/delete", _methodDelete, "DELETE")
				]),
			new NavigationSectionViewModel(
				"Scratch",
				[
					new NavigationItemViewModel("TXT", "notes/ideas.frs", "Freeform request notes", "/scratch/ideas", _methodNeutral),
					new NavigationItemViewModel("RAW", "captures/http.raw", "Stored payload captures", "/scratch/captures", _methodNeutral)
				])
		];

		HistoryItems =
		[
			new HistoryEntryViewModel("POST", "Echo POST", "200 OK in 118 ms", "Today", _methodPost),
			new HistoryEntryViewModel("GET", "Users Feed", "200 OK in 64 ms", "Today", _methodGet),
			new HistoryEntryViewModel("PUT", "Sync Profile", "Draft only", "Not run", _methodPut)
		];

		ResponseHeaderRows =
		[
			new NameValueRowViewModel("content-type", "application/json; charset=utf-8", "response"),
			new NameValueRowViewModel("cache-control", "no-store", "response"),
			new NameValueRowViewModel("x-shell-phase", "phase-1", "response")
		];

		OutputMetrics =
		[
			new OutputMetricViewModel("Status", "200 OK", Color.FromArgb("#1E7A5F")),
			new OutputMetricViewModel("Time", "118 ms", Color.FromArgb("#176AB8")),
			new OutputMetricViewModel("Size", "504 B", Color.FromArgb("#5D6978")),
			new OutputMetricViewModel("Type", "JSON", Color.FromArgb("#176AB8"))
		];

		TraceEntries =
		[
			new TraceEntryViewModel("compile", "request parsed", "12:40:18", _methodNeutral),
			new TraceEntryViewModel("send", "response received", "12:40:18", _methodPost),
			new TraceEntryViewModel("inspect", "body buffered", "12:40:19", _methodGet)
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
	}

	public ObservableCollection<PaneTabViewModel> LeftPaneTabs { get; }

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
				OnPropertyChanged(nameof(RequestStateStatus));
				OnPropertyChanged(nameof(ActiveDocumentSummary));
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
		set => SetProperty(ref _requestEditorText, value);
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

	public string OpenTabsStatus => $"{OpenDocuments.Count} docs  {ResponseState}";

	public string TimingStatus => _isCompactLayout ? "Compact overlay shell" : "Three-pane desktop shell";

	public string ExecutionStatus => "Ready";

	public string ResponseTimeStatus => "118 ms";

	public string ResponseSizeStatus => "504 B";

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
		OnPropertyChanged(nameof(RightSurfaceStatus));
	}

	public void SelectDocument(RequestDocumentViewModel? document)
	{
		if (document is null)
		{
			return;
		}

		foreach (RequestDocumentViewModel item in OpenDocuments)
		{
			item.IsSelected = ReferenceEquals(item, document);
		}

		ApplyRequestSelection(document.Title, document.Method, document.Summary, document.Location);
		SelectExplorerItemByTitle(document.Title);
	}

	public void SelectExplorerItem(NavigationItemViewModel? item)
	{
		if (item is null)
		{
			return;
		}

		foreach (NavigationItemViewModel entry in ExplorerSections.SelectMany(section => section.Items))
		{
			entry.IsSelected = ReferenceEquals(entry, item);
		}

		string method = item.Method ?? SelectedMethod;
		ApplyRequestSelection(item.Title, method, item.Detail, item.Context);
		SelectDocumentByTitle(item.Title);
	}

	private static bool IsTabSelected(IEnumerable<PaneTabViewModel> tabs, string key)
	{
		return tabs.Any(tab => tab.Key == key && tab.IsSelected);
	}

	private void RefreshRequestDraftSignature()
	{
		if (string.IsNullOrWhiteSpace(RequestEditorText))
		{
			return;
		}

		string[] lines = RequestEditorText.Replace("\r\n", "\n").Split('\n');
		for (int index = 0; index < lines.Length; index++)
		{
			if (MethodOptions.Any(method => lines[index].StartsWith($"{method} ", StringComparison.Ordinal)))
			{
				lines[index] = $"{SelectedMethod} {RequestTarget}";
				RequestEditorText = string.Join(Environment.NewLine, lines);
				return;
			}
		}
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
		RequestName = title;
		SelectedMethod = method;
		RequestSummary = summary;
		RequestLocation = location;
		RequestTarget = BuildRequestTarget(location);
		RequestEditorText = BuildRequestEditorText(title, method, RequestTarget);
		HeadersEditorText = BuildHeadersEditorText();
		BodyEditorText = BuildBodyEditorText(title);
		ScriptEditorText = BuildScriptEditorText(title);
		TestsEditorText = BuildTestsEditorText();
		VariablesEditorText = BuildVariablesEditorText();
	}

	private void SelectDocumentByTitle(string title)
	{
		RequestDocumentViewModel? matchingDocument = OpenDocuments.FirstOrDefault(document => document.Title == title);
		if (matchingDocument is null)
		{
			return;
		}

		foreach (RequestDocumentViewModel document in OpenDocuments)
		{
			document.IsSelected = ReferenceEquals(document, matchingDocument);
		}
	}

	private void SelectExplorerItemByTitle(string title)
	{
		NavigationItemViewModel? matchingItem = ExplorerSections
			.SelectMany(section => section.Items)
			.FirstOrDefault(item => item.Title == title);

		if (matchingItem is null)
		{
			return;
		}

		foreach (NavigationItemViewModel item in ExplorerSections.SelectMany(section => section.Items))
		{
			item.IsSelected = ReferenceEquals(item, matchingItem);
		}
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
	}

	private void NotifyOverlayChanged()
	{
		OnPropertyChanged(nameof(IsExplorerOverlayVisible));
		OnPropertyChanged(nameof(IsInspectorOverlayVisible));
		OnPropertyChanged(nameof(IsOverlayBackdropVisible));
	}

	private static string BuildRequestTarget(string location)
	{
		return $"{{base_url}}{location}/{{resource_id}}?trace={{trace_id}}";
	}

	private static string BuildRequestEditorText(string title, string method, string target)
	{
		return string.Join(
			Environment.NewLine,
			[
				"base_url=http://putsomethinghere.what/{workspace_id}/test/12-{request_id}",
				"trace_id={{trace_id}}",
				"resource_id={resource_id}",
				string.Empty,
				$"@request \"{title}\"",
				$"{method} {target}",
				"Accept: application/json",
				"Authorization: Bearer {{access_token}}",
				"X-Workspace: {{workspace_name}}",
				"X-Correlation-Id: 12-{{request_id}}",
				string.Empty,
				"{",
				"  \"firstName\": \"Ada\",",
				"  \"country\": \"Spain\",",
				"  \"age\": 30",
				"}"
			]);
	}

	private static string BuildHeadersEditorText()
	{
		return string.Join(
			Environment.NewLine,
			[
				"Accept: application/json",
				"Content-Type: application/json",
				"X-Trace: shell-reset",
				"X-Environment: local",
				"X-Workbench: editor-first"
			]);
	}

	private static string BuildBodyEditorText(string title)
	{
		return string.Join(
			Environment.NewLine,
			[
				"{",
				$"  \"request\": \"{title}\",",
				"  \"phase\": \"shell-reset\",",
				"  \"surface\": \"editor-first\",",
				"  \"payload\": {",
				"    \"firstName\": \"Ada\",",
				"    \"country\": \"Spain\",",
				"    \"age\": 30",
				"  }",
				"}"
			]);
	}

	private static string BuildScriptEditorText(string title)
	{
		return string.Join(
			Environment.NewLine,
			[
				"beforeSend(ctx) {",
				$"  ctx.vars.requestName = \"{title}\"",
				"  ctx.vars.phase = \"shell-reset\"",
				"  ctx.headers[\"x-shell-surface\"] = \"editor-first\"",
				"  ctx.headers[\"x-environment\"] = ctx.environment.name",
				"}"
			]);
	}

	private static string BuildTestsEditorText()
	{
		return string.Join(
			Environment.NewLine,
			[
				"expect(response.status).toEqual(200)",
				"expect(response.timeMs).toBeLessThan(500)",
				"expect(json(\"$.payload.country\")).toEqual(\"Spain\")",
				"expect(response.headers[\"content-type\"]).toContain(\"json\")"
			]);
	}

	private static string BuildVariablesEditorText()
	{
		return string.Join(
			Environment.NewLine,
			[
				"host = \"api.echo.local\"",
				"workspace = \"echo-lab\"",
				"accent = \"azure\"",
				"region = \"local\"",
				"phase = \"shell-reset\""
			]);
	}

	private static string BuildResponseBodyText()
	{
		return string.Join(
			Environment.NewLine,
			[
				"{",
				"  \"ok\": true,",
				"  \"phase\": \"shell-reset\",",
				"  \"surface\": \"response-pane\",",
				"  \"payload\": {",
				"    \"firstName\": \"Ada\",",
				"    \"country\": \"Spain\",",
				"    \"age\": 30",
				"  }",
				"}"
			]);
	}

	private static string BuildResponseRawText()
	{
		return string.Join(
			Environment.NewLine,
			[
				"HTTP/1.1 200 OK",
				"content-type: application/json; charset=utf-8",
				"cache-control: no-store",
				"x-shell-phase: phase-1",
				string.Empty,
				"{",
				"  \"ok\": true,",
				"  \"phase\": \"shell-reset\"",
				"}"
			]);
	}
}
