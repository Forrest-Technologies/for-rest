using ForRest.Domain;
using ForRest.Plugins.Host;
using ForRest.Repositories;
using ForRest.Services;
using Microsoft.Extensions.Logging;

namespace ForRest.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    #region Private Fields

    private readonly IWorkspaceService workspaceService;
    private readonly IRequestExecutionService requestExecutionService;
    private readonly IExecutionHistoryRepository executionHistoryRepository;
    private readonly VariableResolver variableResolver;
    private readonly JsonEditorService jsonEditorService;
    private readonly PluginCatalog pluginCatalog;
    private readonly ILogger<MainWindowViewModel> logger;
    private AppState state = new();
    private WorkspaceOptionViewModel? selectedWorkspace;
    private EnvironmentOptionViewModel? selectedEnvironment;
    private RequestTabViewModel? selectedRequest = RequestTabViewModel.CreateBlank();
    private string explorerFilter = string.Empty;
    private string responseBody = string.Empty;
    private string responseHeaders = string.Empty;
    private string responseCookies = string.Empty;
    private string responseRaw = string.Empty;
    private string responseSummary = "Idle";
    private string responseStatusText = "Idle";
    private string responseTimeText = "--";
    private string responseSizeText = "--";
    private string responseTypeText = "--";
    private string responseDetailText = "--";
    private string pluginSummary = "Plugins: 0 discovered";
    private bool isBusy;
    private Brush statusBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 63, 142, 252));
    private CancellationTokenSource? executionCancellationTokenSource;

    #endregion

    #region Constructors

    public MainWindowViewModel(
        IWorkspaceService workspaceService,
        IRequestExecutionService requestExecutionService,
        IExecutionHistoryRepository executionHistoryRepository,
        VariableResolver variableResolver,
        JsonEditorService jsonEditorService,
        PluginCatalog pluginCatalog,
        ILogger<MainWindowViewModel> logger)
    {
        this.workspaceService = workspaceService;
        this.requestExecutionService = requestExecutionService;
        this.executionHistoryRepository = executionHistoryRepository;
        this.variableResolver = variableResolver;
        this.jsonEditorService = jsonEditorService;
        this.pluginCatalog = pluginCatalog;
        this.logger = logger;

        LoadCommand = new AsyncRelayCommand(Load);
        SaveCommand = new AsyncRelayCommand(Save);
        SendCommand = new AsyncRelayCommand(Send, () => SelectedRequest is not null && !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        FormatRequestJsonCommand = new RelayCommand(FormatRequestJson, () => SelectedRequest is not null);
        FormatResponseJsonCommand = new RelayCommand(FormatResponseJson, () => !string.IsNullOrWhiteSpace(ResponseBody));
        MinifyResponseJsonCommand = new RelayCommand(MinifyResponseJson, () => !string.IsNullOrWhiteSpace(ResponseBody));
        AddHeaderCommand = new RelayCommand(AddHeader, () => SelectedRequest is not null);
        AddQueryParameterCommand = new RelayCommand(AddQueryParameter, () => SelectedRequest is not null);
        AddVariableCommand = new RelayCommand(AddVariable, () => SelectedRequest is not null);
        AddFormValueCommand = new RelayCommand(AddFormValue, () => SelectedRequest is not null);
    }

    #endregion

    #region Properties

    public ObservableCollection<WorkspaceOptionViewModel> Workspaces { get; } = [];

    public ObservableCollection<EnvironmentOptionViewModel> Environments { get; } = [];

    public ObservableCollection<ExplorerNodeViewModel> ExplorerNodes { get; } = [];

    public ObservableCollection<ExplorerListItemViewModel> ExplorerRows { get; } = [];

    public ObservableCollection<RequestTabViewModel> OpenRequests { get; } = [];

    public ObservableCollection<ExecutionRun> HistoryRuns { get; } = [];

    public ObservableCollection<TestResult> LatestTests { get; } = [];

    public ObservableCollection<ConsoleEntry> LatestConsoleEntries { get; } = [];

    public ObservableCollection<VariableDefinition> LatestRuntimeVariables { get; } = [];

    public ObservableCollection<ResolvedVariable> VariablePreview { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand SendCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand FormatRequestJsonCommand { get; }

    public IRelayCommand FormatResponseJsonCommand { get; }

    public IRelayCommand MinifyResponseJsonCommand { get; }

    public IRelayCommand AddHeaderCommand { get; }

    public IRelayCommand AddQueryParameterCommand { get; }

    public IRelayCommand AddVariableCommand { get; }

    public IRelayCommand AddFormValueCommand { get; }

    public string[] HttpMethods { get; } = Enum.GetNames<HttpMethodKind>();

    public string[] BodyModes { get; } = Enum.GetNames<RequestBodyMode>();

    public string[] AuthModes { get; } = Enum.GetNames<AuthMode>();

    public string[] ApiKeyLocations { get; } = Enum.GetNames<ApiKeyLocation>();

    public WorkspaceOptionViewModel? SelectedWorkspace
    {
        get => selectedWorkspace;
        set => SetProperty(ref selectedWorkspace, value);
    }

    public EnvironmentOptionViewModel? SelectedEnvironment
    {
        get => selectedEnvironment;
        set
        {
            if (SetProperty(ref selectedEnvironment, value))
            {
                RefreshVariablePreview();
            }
        }
    }

    public RequestTabViewModel? SelectedRequest
    {
        get => selectedRequest;
        set
        {
            if (SetProperty(ref selectedRequest, value))
            {
                RefreshVariablePreview();
                SendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ExplorerFilter
    {
        get => explorerFilter;
        set
        {
            if (SetProperty(ref explorerFilter, value))
            {
                RebuildExplorer();
            }
        }
    }

    public string ResponseBody
    {
        get => responseBody;
        private set
        {
            if (SetProperty(ref responseBody, value))
            {
                FormatResponseJsonCommand.NotifyCanExecuteChanged();
                MinifyResponseJsonCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ResponseHeaders
    {
        get => responseHeaders;
        private set => SetProperty(ref responseHeaders, value);
    }

    public string ResponseCookies
    {
        get => responseCookies;
        private set => SetProperty(ref responseCookies, value);
    }

    public string ResponseRaw
    {
        get => responseRaw;
        private set => SetProperty(ref responseRaw, value);
    }

    public string ResponseSummary
    {
        get => responseSummary;
        private set => SetProperty(ref responseSummary, value);
    }

    public string ResponseStatusText
    {
        get => responseStatusText;
        private set => SetProperty(ref responseStatusText, value);
    }

    public string ResponseTimeText
    {
        get => responseTimeText;
        private set => SetProperty(ref responseTimeText, value);
    }

    public string ResponseSizeText
    {
        get => responseSizeText;
        private set => SetProperty(ref responseSizeText, value);
    }

    public string ResponseTypeText
    {
        get => responseTypeText;
        private set => SetProperty(ref responseTypeText, value);
    }

    public string ResponseDetailText
    {
        get => responseDetailText;
        private set => SetProperty(ref responseDetailText, value);
    }

    public Brush StatusBrush
    {
        get => statusBrush;
        private set => SetProperty(ref statusBrush, value);
    }

    public string PluginSummary
    {
        get => pluginSummary;
        private set => SetProperty(ref pluginSummary, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                SendCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    #endregion

    #region Public Methods

    public async Task Load()
    {
        state = await workspaceService.Load();
        PluginSummary = $"Plugins: {pluginCatalog.Discover(Path.Combine(AppContext.BaseDirectory, "Plugins")).Count} discovered";

        Workspaces.ReplaceWith(
        [
            .. state.Workspaces.Select(
                snapshot => new WorkspaceOptionViewModel
                {
                    Snapshot = snapshot,
                }),
        ]);

        var selected = Workspaces.FirstOrDefault();
        if (state.Profile.LastWorkspaceId is { } workspaceId)
        {
            selected = Workspaces.FirstOrDefault(item => item.Snapshot.Workspace.Id == workspaceId) ?? selected;
        }

        if (selected is not null)
        {
            await UseWorkspace(selected);
        }
    }

    public async Task Save()
    {
        PersistOpenRequests();
        await workspaceService.Save(state);

        foreach (var tab in OpenRequests)
        {
            tab.MarkSaved();
        }
    }

    public async Task Send()
    {
        if (SelectedRequest is null || SelectedWorkspace is null)
        {
            return;
        }

        PersistOpenRequests();
        IsBusy = true;
        executionCancellationTokenSource?.Cancel();
        executionCancellationTokenSource = new CancellationTokenSource();

        try
        {
            var workspaceSnapshot = SelectedWorkspace.Snapshot;
            var requestModel = SelectedRequest.ToModel(workspaceSnapshot.Workspace.Id);
            var result = await requestExecutionService.Execute(
                state.Profile,
                workspaceSnapshot,
                requestModel,
                SelectedEnvironment?.Environment,
                executionCancellationTokenSource.Token);

            LatestTests.ReplaceWith(result.Tests);
            LatestConsoleEntries.ReplaceWith(result.ConsoleEntries);
            LatestRuntimeVariables.ReplaceWith(result.RuntimeVariables);
            SetResponse(result.LatestResponse, result.State);

            HistoryRuns.ReplaceWith(await executionHistoryRepository.Load(workspaceSnapshot.Workspace.Id));
            RefreshVariablePreview();
        }
        catch (OperationCanceledException)
        {
            ResponseSummary = "Execution cancelled";
            ResponseStatusText = "Cancelled";
            ResponseTimeText = "--";
            ResponseSizeText = "--";
            ResponseTypeText = "--";
            ResponseDetailText = "--";
            StatusBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 153, 34));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected request execution failure");
            ResponseSummary = exception.Message;
            ResponseStatusText = "Failed";
            ResponseTimeText = "--";
            ResponseSizeText = "--";
            ResponseTypeText = "--";
            ResponseDetailText = "--";
            StatusBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 215, 58, 73));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Cancel()
    {
        executionCancellationTokenSource?.Cancel();
    }

    public async Task UseWorkspace(WorkspaceOptionViewModel? workspace)
    {
        if (workspace is null)
        {
            return;
        }

        SelectedWorkspace = workspace;
        state = state with
        {
            Profile = state.Profile with
            {
                LastWorkspaceId = workspace.Snapshot.Workspace.Id,
            },
        };

        Environments.ReplaceWith(
        [
            .. workspace.Snapshot.Environments.Select(
                environment => new EnvironmentOptionViewModel
                {
                    Environment = environment,
                }),
        ]);

        SelectedEnvironment = Environments.FirstOrDefault(item => item.Environment.Id == workspace.Snapshot.Workspace.ActiveEnvironmentId)
            ?? Environments.FirstOrDefault();

        RebuildExplorer();
        HistoryRuns.ReplaceWith(await executionHistoryRepository.Load(workspace.Snapshot.Workspace.Id));

        OpenRequests.Clear();
        var firstRequest = workspace.Snapshot.Nodes.FirstOrDefault(static item => item.Request is not null)?.Request;
        if (firstRequest is not null)
        {
            OpenRequest(firstRequest.Id);
        }

        ((App)Application.Current).ApplyTheme(workspace.Snapshot.Workspace.Theme);
    }

    public void OpenRequest(Guid requestId)
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        var existing = OpenRequests.FirstOrDefault(item => item.RequestId == requestId);
        if (existing is not null)
        {
            SelectedRequest = existing;
            return;
        }

        var request = SelectedWorkspace.Snapshot.Nodes.FirstOrDefault(item => item.Request?.Id == requestId)?.Request;
        if (request is null)
        {
            return;
        }

        var tab = RequestTabViewModel.FromModel(request);
        tab.PropertyChanged += RequestTabChanged;
        OpenRequests.Add(tab);
        SelectedRequest = tab;
    }

    public void CloseRequest(RequestTabViewModel request)
    {
        if (OpenRequests.Remove(request))
        {
            request.PropertyChanged -= RequestTabChanged;
        }

        SelectedRequest = OpenRequests.LastOrDefault() ?? RequestTabViewModel.CreateBlank();
    }

    public void ShowHistoryRun(ExecutionRun? run)
    {
        if (run is null)
        {
            return;
        }

        LatestTests.ReplaceWith(run.Tests);
        LatestConsoleEntries.ReplaceWith(run.ConsoleEntries);
        LatestRuntimeVariables.ReplaceWith(run.RuntimeVariables);
        SetResponse(run.Response, run.State);
    }

    public void FormatRequestJson()
    {
        if (SelectedRequest is null || !string.Equals(SelectedRequest.BodyMode, RequestBodyMode.Json.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var result = jsonEditorService.Format(SelectedRequest.BodyContent);
        if (result.Succeeded && result.Value is not null)
        {
            SelectedRequest.BodyContent = result.Value;
        }
    }

    public void FormatResponseJson()
    {
        var result = jsonEditorService.Format(ResponseBody);
        if (result.Succeeded && result.Value is not null)
        {
            ResponseBody = result.Value;
        }
    }

    public void MinifyResponseJson()
    {
        var result = jsonEditorService.Minify(ResponseBody);
        if (result.Succeeded && result.Value is not null)
        {
            ResponseBody = result.Value;
        }
    }

    public void AddHeader()
    {
        SelectedRequest?.Headers.Add(new KeyValueItemViewModel());
    }

    public void AddQueryParameter()
    {
        SelectedRequest?.QueryParameters.Add(new KeyValueItemViewModel());
    }

    public void AddVariable()
    {
        SelectedRequest?.Variables.Add(new VariableItemViewModel());
    }

    public void AddFormValue()
    {
        SelectedRequest?.FormValues.Add(new KeyValueItemViewModel());
    }

    #endregion

    #region Private Methods

    private static IEnumerable<VariableDefinition> BuildSystemVariables()
    {
        return Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(static item => item.Key is not null && item.Value is not null)
            .Select(
                static item => new VariableDefinition
                {
                    Key = item.Key.ToString() ?? string.Empty,
                    Value = item.Value?.ToString() ?? string.Empty,
                    Scope = VariableScope.System,
                });
    }

    private void PersistOpenRequests()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        var snapshot = SelectedWorkspace.Snapshot;
        var updatedNodes = snapshot.Nodes.ToList();
        foreach (var openRequest in OpenRequests)
        {
            var updatedRequest = openRequest.ToModel(snapshot.Workspace.Id);
            var index = updatedNodes.FindIndex(item => item.Request?.Id == openRequest.RequestId);
            if (index >= 0)
            {
                updatedNodes[index] = updatedNodes[index] with
                {
                    Name = updatedRequest.Name,
                    Request = updatedRequest,
                };
            }
        }

        var updatedWorkspace = snapshot with
        {
            Nodes = updatedNodes,
            Workspace = snapshot.Workspace with
            {
                ActiveEnvironmentId = SelectedEnvironment?.Environment.Id,
            },
        };

        state = state with
        {
            Workspaces = state.Workspaces.Select(item => item.Workspace.Id == updatedWorkspace.Workspace.Id ? updatedWorkspace : item).ToList(),
            Profile = state.Profile with
            {
                LastWorkspaceId = updatedWorkspace.Workspace.Id,
            },
        };

        var selectedIndex = Workspaces.ToList().FindIndex(item => item.Snapshot.Workspace.Id == updatedWorkspace.Workspace.Id);
        if (selectedIndex >= 0)
        {
            Workspaces[selectedIndex] = new WorkspaceOptionViewModel
            {
                Snapshot = updatedWorkspace,
            };
            SelectedWorkspace = Workspaces[selectedIndex];
        }
    }

    private void RefreshVariablePreview()
    {
        VariablePreview.Clear();
        if (SelectedWorkspace is null || SelectedRequest is null)
        {
            return;
        }

        var requestModel = SelectedRequest.ToModel(SelectedWorkspace.Snapshot.Workspace.Id);
        var preview = variableResolver.Resolve(
            BuildSystemVariables(),
            state.Profile.GlobalVariables,
            SelectedWorkspace.Snapshot.Workspace.Variables,
            SelectedEnvironment?.Environment.Variables ?? [],
            requestModel.Variables,
            LatestRuntimeVariables);

        VariablePreview.ReplaceWith(preview);
    }

    private void RebuildExplorer()
    {
        ExplorerNodes.Clear();
        ExplorerRows.Clear();
        if (SelectedWorkspace is null)
        {
            return;
        }

        var lookup = SelectedWorkspace.Snapshot.Nodes.ToLookup(static item => item.ParentId);
        var children = BuildChildren(lookup, null).ToList();
        ExplorerNodes.ReplaceWith(children);
        ExplorerRows.ReplaceWith(FlattenChildren(children, 0));
    }

    private IEnumerable<ExplorerNodeViewModel> BuildChildren(ILookup<Guid?, WorkspaceNodeDefinition> lookup, Guid? parentId)
    {
        foreach (var node in lookup[parentId].OrderBy(static item => item.SortOrder))
        {
            var matchesFilter = string.IsNullOrWhiteSpace(ExplorerFilter)
                || node.Name.Contains(ExplorerFilter, StringComparison.OrdinalIgnoreCase)
                || (node.Request?.UrlTemplate.Contains(ExplorerFilter, StringComparison.OrdinalIgnoreCase) ?? false);

            var children = BuildChildren(lookup, node.Id).ToList();
            if (!matchesFilter && children.Count == 0)
            {
                continue;
            }

            yield return new ExplorerNodeViewModel
            {
                Id = node.Id,
                Name = node.Name,
                Kind = node.Kind,
                Request = node.Request,
                Children = new ObservableCollection<ExplorerNodeViewModel>(children),
            };
        }
    }

    private IEnumerable<ExplorerListItemViewModel> FlattenChildren(IEnumerable<ExplorerNodeViewModel> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            yield return new()
            {
                Id = node.Id,
                RequestId = node.Request?.Id,
                Name = node.Name,
                DisplayName = $"{new string(' ', depth * 2)}{node.Name}",
                Kind = node.Kind,
                BadgeText = node.Kind switch
                {
                    WorkspaceNodeKind.Request when node.Request is not null => node.Request.Method.ToString().ToUpperInvariant(),
                    WorkspaceNodeKind.Collection => "COL",
                    WorkspaceNodeKind.Folder => "DIR",
                    _ => "REQ",
                },
                SecondaryText = node.Request is not null ? ShellVisuals.UrlHost(node.Request.UrlTemplate) : node.Kind.ToString(),
                BadgeBrush = node.Request is not null
                    ? ShellVisuals.MethodBrush(node.Request.Method.ToString())
                    : ShellVisuals.NeutralBrush(),
            };

            foreach (var child in FlattenChildren(node.Children, depth + 1))
            {
                yield return child;
            }
        }
    }

    private void RequestTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (sender == SelectedRequest)
        {
            RefreshVariablePreview();
        }
    }

    private void SetResponse(ResponseSnapshot? response, ExecutionState state)
    {
        if (response is null)
        {
            ResponseBody = string.Empty;
            ResponseHeaders = string.Empty;
            ResponseCookies = string.Empty;
            ResponseRaw = string.Empty;
            ResponseSummary = state == ExecutionState.Failed ? "Request failed" : "No response";
            ResponseStatusText = state == ExecutionState.Failed ? "Failed" : "Idle";
            ResponseTimeText = "--";
            ResponseSizeText = "--";
            ResponseTypeText = "--";
            ResponseDetailText = "--";
            StatusBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 215, 58, 73));
            return;
        }

        ResponseBody = response.Body;
        ResponseHeaders = string.Join(Environment.NewLine, response.Headers.Select(static item => $"{item.Key}: {item.Value}"));
        ResponseCookies = string.Join(Environment.NewLine, response.Cookies.Select(static item => item.Value));
        ResponseRaw = response.RawResponse;
        ResponseSummary = $"{response.StatusCode} in {response.DurationMilliseconds} ms, {response.SizeBytes} bytes";
        ResponseStatusText = $"{response.StatusCode} {response.ReasonPhrase}".Trim();
        ResponseTimeText = $"{response.DurationMilliseconds} ms";
        ResponseSizeText = FormatSize(response.SizeBytes);
        ResponseTypeText = string.IsNullOrWhiteSpace(response.ContentType) ? "unknown" : response.ContentType;
        ResponseDetailText = $"{response.Headers.Count} headers  {response.Cookies.Count} cookies";
        StatusBrush = new SolidColorBrush(
            response.StatusCode switch
            {
                >= 200 and < 300 => Windows.UI.Color.FromArgb(255, 35, 165, 90),
                >= 300 and < 400 => Windows.UI.Color.FromArgb(255, 210, 153, 34),
                _ => Windows.UI.Color.FromArgb(255, 215, 58, 73),
            });
    }

    private static string FormatSize(long sizeBytes)
    {
        return sizeBytes switch
        {
            >= 1_048_576 => $"{sizeBytes / 1_048_576d:0.##} MB",
            >= 1_024 => $"{sizeBytes / 1_024d:0.##} KB",
            _ => $"{sizeBytes} B",
        };
    }

    #endregion
}

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
