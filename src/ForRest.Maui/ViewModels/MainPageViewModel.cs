using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ForRest.Maui.ViewModels;

public sealed class MainPageViewModel : ObservableObject
{
	private const double DefaultLeftPanePixels = 304d;
	private const double DefaultRightPanePixels = 340d;
	private const double MinLeftPanePixels = 220d;
	private const double MinRightPanePixels = 240d;
	private const double MinCenterPanePixels = 360d;
	private const double SplitterPixels = 6d;

	private readonly Color _methodGet = Color.FromArgb("#1565C0");
	private readonly Color _methodPost = Color.FromArgb("#2E7D32");
	private readonly Color _methodPut = Color.FromArgb("#00838F");
	private readonly Color _methodDelete = Color.FromArgb("#C62828");
	private readonly Color _methodOpt = Color.FromArgb("#616161");

	private double _leftPanePixels = DefaultLeftPanePixels;
	private double _rightPanePixels = DefaultRightPanePixels;
	private double _leftRestorePixels = DefaultLeftPanePixels;
	private double _rightRestorePixels = DefaultRightPanePixels;
	private bool _leftPaneCollapsed;
	private bool _rightPaneCollapsed;
	private string _selectedWorkspace;
	private string _selectedEnvironment;
	private string _selectedMethod;
	private string _requestUrl;
	private string _requestName;
	private string _requestNotes;
	private string _bodyEditorText;
	private string _testsEditorText;
	private string _responseBodyText;
	private string _responseRawText;
	private string _responseState;

	public MainPageViewModel()
	{
		WorkspaceOptions =
		[
			"Echo Workspace",
			"Local Demo",
			"API Playground"
		];
		EnvironmentOptions =
		[
			"Develop",
			"Stage",
			"Prod"
		];
		MethodOptions =
		[
			"GET",
			"POST",
			"PUT",
			"DELETE",
			"OPTIONS"
		];

		_selectedWorkspace = WorkspaceOptions[0];
		_selectedEnvironment = EnvironmentOptions[0];
		_selectedMethod = "POST";
		_requestUrl = "https://{{host}}/echo/post?age={{age}}&country={{country}}";
		_requestName = "NativeRest Echo POST";
		_requestNotes = "Minimal shell draft. Keep the structure compact and textual before adding deeper request features.";
		_bodyEditorText = "{\n  \"firstName\": \"Ada\",\n  \"country\": \"Spain\",\n  \"age\": 30\n}";
		_testsEditorText = "# Check response time\nresponse.time < 2000\n\n# Check status\nresponse.status == 200";
		_responseBodyText = "{\n  \"args\": {\n    \"age\": \"30\",\n    \"country\": \"Spain\"\n  },\n  \"json\": {\n    \"firstName\": \"Ada\",\n    \"country\": \"Spain\",\n    \"age\": 30\n  }\n}";
		_responseRawText = "HTTP/1.1 200 OK\ncontent-type: application/json\ncontent-length: 504\n\n{\n  \"args\": {\n    \"age\": \"30\",\n    \"country\": \"Spain\"\n  }\n}";
		_responseState = "Idle";

		LeftPaneTabs =
		[
			new PaneTabViewModel("explorer", "Explorer", true),
			new PaneTabViewModel("history", "History")
		];

		OpenDocuments =
		[
			new RequestDocumentViewModel("Echo POST", "POST", "Active request", true, true),
			new RequestDocumentViewModel("Text XML", "GET", "Secondary placeholder", false)
		];

		CenterTabs =
		[
			new PaneTabViewModel("request", "Request", true),
			new PaneTabViewModel("body", "Body"),
			new PaneTabViewModel("tests", "Tests")
		];

		RightPaneTabs =
		[
			new PaneTabViewModel("body", "Body", true),
			new PaneTabViewModel("headers", "Headers"),
			new PaneTabViewModel("raw", "Raw")
		];

		ExplorerItems =
		[
			new ExplorerItemViewModel("GET", "Get Test Xml", "Simple XML smoke request", _methodGet),
			new ExplorerItemViewModel("POST", "NativeRest Echo POST", "Multipart and JSON echo", _methodPost, true),
			new ExplorerItemViewModel("GET", "NativeRest Load Test", "Textual request draft", _methodGet),
			new ExplorerItemViewModel("POST", "Cookies Test", "Cookie handling stub", _methodPost),
			new ExplorerItemViewModel("PUT", "New PUT Request", "Empty request shell", _methodPut),
			new ExplorerItemViewModel("DEL", "Delete Users", "Dangerous placeholder", _methodDelete),
			new ExplorerItemViewModel("OPT", "Post Form Data", "Form preview", _methodOpt)
		];

		HistoryItems =
		[
			new HistoryEntryViewModel("POST", "NativeRest Echo POST", "200 OK in 9.06 ms", "Today", _methodPost),
			new HistoryEntryViewModel("GET", "Get Test Xml", "200 OK in 18.44 ms", "Today", _methodGet),
			new HistoryEntryViewModel("PUT", "New PUT Request", "No execution yet", "Draft", _methodPut)
		];

		ResponseHeaderRows =
		[
			new NameValueRowViewModel("content-type", "application/json", "response"),
			new NameValueRowViewModel("content-length", "504", "response"),
			new NameValueRowViewModel("server", "nginx/1.18.0", "response")
		];
	}

	public IReadOnlyList<string> WorkspaceOptions { get; }

	public IReadOnlyList<string> EnvironmentOptions { get; }

	public IReadOnlyList<string> MethodOptions { get; }

	public ObservableCollection<PaneTabViewModel> LeftPaneTabs { get; }

	public ObservableCollection<RequestDocumentViewModel> OpenDocuments { get; }

	public ObservableCollection<PaneTabViewModel> CenterTabs { get; }

	public ObservableCollection<PaneTabViewModel> RightPaneTabs { get; }

	public ObservableCollection<ExplorerItemViewModel> ExplorerItems { get; }

	public ObservableCollection<HistoryEntryViewModel> HistoryItems { get; }

	public ObservableCollection<NameValueRowViewModel> ResponseHeaderRows { get; }

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
		set => SetProperty(ref _selectedEnvironment, value);
	}

	public string SelectedMethod
	{
		get => _selectedMethod;
		set => SetProperty(ref _selectedMethod, value);
	}

	public string RequestUrl
	{
		get => _requestUrl;
		set => SetProperty(ref _requestUrl, value);
	}

	public string RequestName
	{
		get => _requestName;
		set => SetProperty(ref _requestName, value);
	}

	public string RequestNotes
	{
		get => _requestNotes;
		set => SetProperty(ref _requestNotes, value);
	}

	public string BodyEditorText
	{
		get => _bodyEditorText;
		set => SetProperty(ref _bodyEditorText, value);
	}

	public string TestsEditorText
	{
		get => _testsEditorText;
		set => SetProperty(ref _testsEditorText, value);
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
		set => SetProperty(ref _responseState, value);
	}

	public bool IsLeftPaneVisible => !_leftPaneCollapsed;

	public bool IsRightPaneVisible => !_rightPaneCollapsed;

	public GridLength LeftPaneWidth => new(_leftPaneCollapsed ? 0d : _leftPanePixels, GridUnitType.Absolute);

	public GridLength RightPaneWidth => new(_rightPaneCollapsed ? 0d : _rightPanePixels, GridUnitType.Absolute);

	public GridLength LeftSplitterWidth => new(_leftPaneCollapsed ? 0d : SplitterPixels, GridUnitType.Absolute);

	public GridLength RightSplitterWidth => new(_rightPaneCollapsed ? 0d : SplitterPixels, GridUnitType.Absolute);

	public string WorkspaceBadge => SelectedWorkspace;

	public string LayoutSummary => $"L {_leftPanePixels:0}px  C dominant  R {_rightPanePixels:0}px";

	public string RequestStateStatus => "Idle";

	public string OpenTabsStatus => $"{OpenDocuments.Count} request tabs";

	public string TimingStatus => "Pane view";

	public bool IsExplorerTabVisible => IsTabSelected(LeftPaneTabs, "explorer");

	public bool IsHistoryTabVisible => IsTabSelected(LeftPaneTabs, "history");

	public bool IsRequestTabVisible => IsTabSelected(CenterTabs, "request");

	public bool IsBodyTabVisible => IsTabSelected(CenterTabs, "body");

	public bool IsTestsTabVisible => IsTabSelected(CenterTabs, "tests");

	public bool IsInspectorBodyVisible => IsTabSelected(RightPaneTabs, "body");

	public bool IsInspectorHeadersVisible => IsTabSelected(RightPaneTabs, "headers");

	public bool IsInspectorRawVisible => IsTabSelected(RightPaneTabs, "raw");

	public void ToggleLeftPane()
	{
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

	public void ResizeLeftPane(double requestedWidth, double totalWidth)
	{
		if (_leftPaneCollapsed)
		{
			return;
		}

		_leftPanePixels = ClampLeftPane(requestedWidth, totalWidth);
		NotifyPaneLayoutChanged();
	}

	public void ResizeRightPane(double requestedWidth, double totalWidth)
	{
		if (_rightPaneCollapsed)
		{
			return;
		}

		_rightPanePixels = ClampRightPane(requestedWidth, totalWidth);
		NotifyPaneLayoutChanged();
	}

	public void ConstrainPaneLayout(double totalWidth)
	{
		if (totalWidth <= 0)
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
		OnPropertyChanged(nameof(IsBodyTabVisible));
		OnPropertyChanged(nameof(IsTestsTabVisible));
	}

	public void SelectRightPaneTab(PaneTabViewModel? tab)
	{
		if (tab is null)
		{
			return;
		}

		SetSelected(RightPaneTabs, tab);
		OnPropertyChanged(nameof(IsInspectorBodyVisible));
		OnPropertyChanged(nameof(IsInspectorHeadersVisible));
		OnPropertyChanged(nameof(IsInspectorRawVisible));
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

		RequestName = document.Title;
		SelectedMethod = document.Method;
		RequestNotes = document.Summary;
	}

	public void SelectExplorerItem(ExplorerItemViewModel? item)
	{
		if (item is null)
		{
			return;
		}

		foreach (ExplorerItemViewModel entry in ExplorerItems)
		{
			entry.IsSelected = ReferenceEquals(entry, item);
		}

		RequestName = item.Title;
		SelectedMethod = item.Kind switch
		{
			"DEL" => "DELETE",
			"OPT" => "OPTIONS",
			_ => item.Kind
		};
		RequestNotes = item.Detail;
	}

	private static bool IsTabSelected(IEnumerable<PaneTabViewModel> tabs, string key)
	{
		return tabs.Any(tab => tab.Key == key && tab.IsSelected);
	}

	private void SetSelected(IEnumerable<PaneTabViewModel> tabs, PaneTabViewModel selected)
	{
		foreach (PaneTabViewModel tab in tabs)
		{
			tab.IsSelected = ReferenceEquals(tab, selected);
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
		OnPropertyChanged(nameof(LayoutSummary));
	}
}
