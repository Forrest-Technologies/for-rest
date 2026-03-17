using System.Collections.Specialized;
using System.ComponentModel;

namespace ForRest.App.ViewModels;

public sealed class WorkspaceOptionViewModel
{
    public required WorkspaceSnapshot Snapshot { get; init; }

    public string Name => Snapshot.Workspace.Name;
}

public sealed class EnvironmentOptionViewModel
{
    public required EnvironmentDefinition Environment { get; init; }

    public string Name => Environment.Name;
}

public sealed class ExplorerNodeViewModel
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public WorkspaceNodeKind Kind { get; init; }

    public RequestDefinition? Request { get; init; }

    public ObservableCollection<ExplorerNodeViewModel> Children { get; init; } = [];
}

public sealed class ExplorerListItemViewModel
{
    public Guid Id { get; init; }

    public Guid? RequestId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public WorkspaceNodeKind Kind { get; init; }

    public string BadgeText { get; init; } = "REQ";

    public string SecondaryText { get; init; } = string.Empty;

    public Brush BadgeBrush { get; init; } = ShellVisuals.NeutralBrush();
}

public sealed class KeyValueItemViewModel : ObservableObject
{
    #region Private Fields

    private string key = string.Empty;
    private string value = string.Empty;
    private bool isEnabled = true;
    private string description = string.Empty;

    #endregion

    #region Properties

    public string Key
    {
        get => key;
        set => SetProperty(ref key, value);
    }

    public string Value
    {
        get => this.value;
        set => SetProperty(ref this.value, value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    #endregion

    #region Public Methods

    public KeyValueDefinition ToModel()
    {
        return new()
        {
            Key = Key,
            Value = Value,
            IsEnabled = IsEnabled,
            Description = Description,
        };
    }

    public static KeyValueItemViewModel FromModel(KeyValueDefinition model)
    {
        return new()
        {
            Key = model.Key,
            Value = model.Value,
            IsEnabled = model.IsEnabled,
            Description = model.Description,
        };
    }

    #endregion
}

public sealed class VariableItemViewModel : ObservableObject
{
    #region Private Fields

    private string key = string.Empty;
    private string value = string.Empty;
    private VariableScope scope = VariableScope.RequestLocal;
    private bool isSecret;
    private string description = string.Empty;
    private bool isEnabled = true;

    #endregion

    #region Properties

    public string Key
    {
        get => key;
        set => SetProperty(ref key, value);
    }

    public string Value
    {
        get => this.value;
        set => SetProperty(ref this.value, value);
    }

    public VariableScope Scope
    {
        get => scope;
        set => SetProperty(ref scope, value);
    }

    public bool IsSecret
    {
        get => isSecret;
        set => SetProperty(ref isSecret, value);
    }

    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    #endregion

    #region Public Methods

    public VariableDefinition ToModel()
    {
        return new()
        {
            Key = Key,
            Value = Value,
            Scope = Scope,
            IsSecret = IsSecret,
            Description = Description,
            IsEnabled = IsEnabled,
        };
    }

    public static VariableItemViewModel FromModel(VariableDefinition model)
    {
        return new()
        {
            Key = model.Key,
            Value = model.Value,
            Scope = model.Scope,
            IsSecret = model.IsSecret,
            Description = model.Description,
            IsEnabled = model.IsEnabled,
        };
    }

    #endregion
}

public sealed class RequestTabViewModel : ObservableObject
{
    #region Private Fields

    private string name = "Untitled Request";
    private string method = HttpMethodKind.Get.ToString();
    private string url = "https://";
    private Brush methodBadgeBrush = ShellVisuals.MethodBrush(HttpMethodKind.Get.ToString());
    private string authMode = Models.AuthMode.None.ToString();
    private string apiKeyLocation = Models.ApiKeyLocation.Header.ToString();
    private string username = string.Empty;
    private string password = string.Empty;
    private string bearerToken = string.Empty;
    private string apiKeyName = string.Empty;
    private string apiKeyValue = string.Empty;
    private string bodyMode = RequestBodyMode.None.ToString();
    private string bodyContent = string.Empty;
    private string contentType = "application/json";
    private string preRequestScript = string.Empty;
    private string testsScript = string.Empty;
    private int repeatCount = 1;
    private int delayMilliseconds;
    private int intervalMilliseconds;
    private int timeoutMilliseconds = 30_000;
    private bool followRedirects = true;
    private bool validateSsl = true;
    private bool saveResponseToHistory = true;
    private bool isDirty;

    #endregion

    #region Constructors

    public RequestTabViewModel()
    {
        QueryParameters.CollectionChanged += CollectionChanged;
        Headers.CollectionChanged += CollectionChanged;
        Variables.CollectionChanged += CollectionChanged;
        FormValues.CollectionChanged += CollectionChanged;
    }

    #endregion

    #region Properties

    public Guid RequestId { get; init; } = Guid.NewGuid();

    public string Name
    {
        get => name;
        set => SetAndDirty(ref name, value);
    }

    public string Method
    {
        get => method;
        set
        {
            if (SetProperty(ref method, value))
            {
                MethodBadgeBrush = ShellVisuals.MethodBrush(value);
                IsDirty = true;
                RaiseSummaryPropertiesChanged();
            }
        }
    }

    public string Url
    {
        get => url;
        set
        {
            if (SetProperty(ref url, value))
            {
                OnPropertyChanged(nameof(UrlHost));
                IsDirty = true;
                RaiseSummaryPropertiesChanged();
            }
        }
    }

    public Brush MethodBadgeBrush
    {
        get => methodBadgeBrush;
        private set => SetProperty(ref methodBadgeBrush, value);
    }

    public string AuthMode
    {
        get => authMode;
        set => SetAndDirty(ref authMode, value);
    }

    public string ApiKeyLocation
    {
        get => apiKeyLocation;
        set => SetAndDirty(ref apiKeyLocation, value);
    }

    public string Username
    {
        get => username;
        set => SetAndDirty(ref username, value);
    }

    public string Password
    {
        get => password;
        set => SetAndDirty(ref password, value);
    }

    public string BearerToken
    {
        get => bearerToken;
        set => SetAndDirty(ref bearerToken, value);
    }

    public string ApiKeyName
    {
        get => apiKeyName;
        set => SetAndDirty(ref apiKeyName, value);
    }

    public string ApiKeyValue
    {
        get => apiKeyValue;
        set => SetAndDirty(ref apiKeyValue, value);
    }

    public string BodyMode
    {
        get => bodyMode;
        set => SetAndDirty(ref bodyMode, value);
    }

    public string BodyContent
    {
        get => bodyContent;
        set => SetAndDirty(ref bodyContent, value);
    }

    public string ContentType
    {
        get => contentType;
        set => SetAndDirty(ref contentType, value);
    }

    public string PreRequestScript
    {
        get => preRequestScript;
        set => SetAndDirty(ref preRequestScript, value);
    }

    public string TestsScript
    {
        get => testsScript;
        set => SetAndDirty(ref testsScript, value);
    }

    public int RepeatCount
    {
        get => repeatCount;
        set => SetAndDirty(ref repeatCount, value);
    }

    public int DelayMilliseconds
    {
        get => delayMilliseconds;
        set => SetAndDirty(ref delayMilliseconds, value);
    }

    public int IntervalMilliseconds
    {
        get => intervalMilliseconds;
        set => SetAndDirty(ref intervalMilliseconds, value);
    }

    public int TimeoutMilliseconds
    {
        get => timeoutMilliseconds;
        set => SetAndDirty(ref timeoutMilliseconds, value);
    }

    public bool FollowRedirects
    {
        get => followRedirects;
        set => SetAndDirty(ref followRedirects, value);
    }

    public bool ValidateSsl
    {
        get => validateSsl;
        set => SetAndDirty(ref validateSsl, value);
    }

    public bool SaveResponseToHistory
    {
        get => saveResponseToHistory;
        set => SetAndDirty(ref saveResponseToHistory, value);
    }

    public bool IsDirty
    {
        get => isDirty;
        set
        {
            if (SetProperty(ref isDirty, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string DisplayTitle => IsDirty ? $"{Name} *" : Name;

    public string UrlHost => ShellVisuals.UrlHost(Url);

    public int EnabledHeaderCount => Headers.Count(static item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Key));

    public int EnabledQueryParameterCount => QueryParameters.Count(static item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Key));

    public int VariableCount => Variables.Count(static item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Key));

    public string RequestSummary => $"{EnabledHeaderCount} hdr  {EnabledQueryParameterCount} params  {VariableCount} vars  {BodyModeDisplay} body";

    public string RuntimeOptionsSummary => $"{(ValidateSsl ? "ssl on" : "ssl off")}  {(FollowRedirects ? "redirects on" : "redirects off")}  timeout {TimeoutMilliseconds} ms";

    public string BodyModeDisplay => BodyMode switch
    {
        nameof(RequestBodyMode.FormUrlEncoded) => "form",
        nameof(RequestBodyMode.MultipartFormData) => "multipart",
        nameof(RequestBodyMode.RawText) => "raw",
        nameof(RequestBodyMode.Json) => "json",
        _ => "none",
    };

    public ObservableCollection<KeyValueItemViewModel> QueryParameters { get; } = [];

    public ObservableCollection<KeyValueItemViewModel> Headers { get; } = [];

    public ObservableCollection<KeyValueItemViewModel> FormValues { get; } = [];

    public ObservableCollection<VariableItemViewModel> Variables { get; } = [];

    #endregion

    #region Public Methods

    public void MarkSaved()
    {
        IsDirty = false;
    }

    public RequestDefinition ToModel(Guid workspaceId)
    {
        return new()
        {
            Id = RequestId,
            WorkspaceId = workspaceId,
            Name = Name,
            Method = Enum.TryParse<HttpMethodKind>(Method, true, out var httpMethod) ? httpMethod : HttpMethodKind.Get,
            UrlTemplate = Url,
            QueryParameters = [.. QueryParameters.Select(static item => item.ToModel())],
            Headers = [.. Headers.Select(static item => item.ToModel())],
            Auth = new()
            {
                Mode = Enum.TryParse<Models.AuthMode>(AuthMode, true, out var authSelection) ? authSelection : Models.AuthMode.None,
                Username = Username,
                Password = Password,
                BearerToken = BearerToken,
                ApiKeyName = ApiKeyName,
                ApiKeyValue = ApiKeyValue,
                ApiKeyLocation = Enum.TryParse<Models.ApiKeyLocation>(ApiKeyLocation, true, out var apiKeySelection) ? apiKeySelection : Models.ApiKeyLocation.Header,
            },
            Body = new()
            {
                Mode = Enum.TryParse<RequestBodyMode>(BodyMode, true, out var requestBodyMode) ? requestBodyMode : RequestBodyMode.None,
                RawContent = BodyContent,
                ContentType = ContentType,
                FormValues = [.. FormValues.Select(static item => item.ToModel())],
            },
            Variables = [.. Variables.Select(static item => item.ToModel())],
            PreRequestScript = PreRequestScript,
            TestsScript = TestsScript,
            Schedule = new()
            {
                RepeatCount = Math.Max(1, RepeatCount),
                DelayMilliseconds = Math.Max(0, DelayMilliseconds),
                IntervalMilliseconds = Math.Max(0, IntervalMilliseconds),
            },
            TimeoutMilliseconds = Math.Max(1, TimeoutMilliseconds),
            FollowRedirects = FollowRedirects,
            ValidateSsl = ValidateSsl,
            SaveResponseToHistory = SaveResponseToHistory,
            Extractions = [],
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
    }

    public static RequestTabViewModel CreateBlank()
    {
        return new();
    }

    public static RequestTabViewModel FromModel(RequestDefinition model)
    {
        var request = new RequestTabViewModel
        {
            RequestId = model.Id,
            Name = model.Name,
            Method = model.Method.ToString(),
            Url = model.UrlTemplate,
            AuthMode = model.Auth.Mode.ToString(),
            ApiKeyLocation = model.Auth.ApiKeyLocation.ToString(),
            Username = model.Auth.Username,
            Password = model.Auth.Password,
            BearerToken = model.Auth.BearerToken,
            ApiKeyName = model.Auth.ApiKeyName,
            ApiKeyValue = model.Auth.ApiKeyValue,
            BodyMode = model.Body.Mode.ToString(),
            BodyContent = model.Body.RawContent,
            ContentType = string.IsNullOrWhiteSpace(model.Body.ContentType) ? "application/json" : model.Body.ContentType,
            PreRequestScript = model.PreRequestScript,
            TestsScript = model.TestsScript,
            RepeatCount = model.Schedule.RepeatCount,
            DelayMilliseconds = model.Schedule.DelayMilliseconds,
            IntervalMilliseconds = model.Schedule.IntervalMilliseconds,
            TimeoutMilliseconds = model.TimeoutMilliseconds,
            FollowRedirects = model.FollowRedirects,
            ValidateSsl = model.ValidateSsl,
            SaveResponseToHistory = model.SaveResponseToHistory,
        };

        foreach (var item in model.QueryParameters)
        {
            request.QueryParameters.Add(KeyValueItemViewModel.FromModel(item));
        }

        foreach (var item in model.Headers)
        {
            request.Headers.Add(KeyValueItemViewModel.FromModel(item));
        }

        foreach (var item in model.Body.FormValues)
        {
            request.FormValues.Add(KeyValueItemViewModel.FromModel(item));
        }

        foreach (var item in model.Variables)
        {
            request.Variables.Add(VariableItemViewModel.FromModel(item));
        }

        request.MarkSaved();
        request.MethodBadgeBrush = ShellVisuals.MethodBrush(request.Method);
        return request;
    }

    #endregion

    #region Private Methods

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
        {
            foreach (var item in args.NewItems.OfType<INotifyPropertyChanged>())
            {
                item.PropertyChanged += NestedPropertyChanged;
            }
        }

        if (args.OldItems is not null)
        {
            foreach (var item in args.OldItems.OfType<INotifyPropertyChanged>())
            {
                item.PropertyChanged -= NestedPropertyChanged;
            }
        }

        IsDirty = true;
        RaiseSummaryPropertiesChanged();
    }

    private void NestedPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        IsDirty = true;
        RaiseSummaryPropertiesChanged();
    }

    private void SetAndDirty<T>(ref T backingField, T value)
    {
        if (SetProperty(ref backingField, value))
        {
            IsDirty = true;
            RaiseSummaryPropertiesChanged();
        }
    }

    private void RaiseSummaryPropertiesChanged()
    {
        OnPropertyChanged(nameof(EnabledHeaderCount));
        OnPropertyChanged(nameof(EnabledQueryParameterCount));
        OnPropertyChanged(nameof(VariableCount));
        OnPropertyChanged(nameof(RequestSummary));
        OnPropertyChanged(nameof(RuntimeOptionsSummary));
        OnPropertyChanged(nameof(BodyModeDisplay));
    }

    #endregion
}

internal static class ShellVisuals
{
    public static Brush MethodBrush(string method)
    {
        return new SolidColorBrush(
            method.ToUpperInvariant() switch
            {
                "GET" => Windows.UI.Color.FromArgb(255, 32, 155, 117),
                "POST" => Windows.UI.Color.FromArgb(255, 45, 118, 255),
                "PUT" => Windows.UI.Color.FromArgb(255, 108, 87, 255),
                "PATCH" => Windows.UI.Color.FromArgb(255, 208, 119, 34),
                "DELETE" => Windows.UI.Color.FromArgb(255, 213, 66, 94),
                "HEAD" => Windows.UI.Color.FromArgb(255, 118, 134, 158),
                "OPTIONS" => Windows.UI.Color.FromArgb(255, 43, 151, 255),
                _ => Windows.UI.Color.FromArgb(255, 95, 107, 129),
            });
    }

    public static Brush NeutralBrush()
    {
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 91, 111));
    }

    public static string UrlHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host
            : "unsaved request";
    }
}
