namespace ForRest.Models;

public sealed record PaneLayoutPreference
{
    public double LeftPaneWidth { get; init; } = 280;

    public double MiddlePaneWidth { get; init; } = 680;

    public double RightPaneWidth { get; init; } = 420;

    public double EditorFontSize { get; init; } = 13;

    public bool UseCodeFont { get; init; } = true;
}

public sealed record WorkspaceSettings
{
    public bool UseAccentColor { get; init; } = true;

    public bool EnablePortablePaths { get; init; }

    public PaneLayoutPreference PaneLayout { get; init; } = new();
}

public sealed record WorkspaceDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public Guid? ActiveEnvironmentId { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<ThemeKind>))]
    public ThemeKind Theme { get; init; } = ThemeKind.System;

    public List<VariableDefinition> Variables { get; init; } = [];

    public WorkspaceSettings Settings { get; init; } = new();

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record WorkspaceNodeDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid WorkspaceId { get; init; }

    public Guid? ParentId { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<WorkspaceNodeKind>))]
    public WorkspaceNodeKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public bool IsPinned { get; init; }

    public RequestDefinition? Request { get; init; }
}

public sealed record EnvironmentDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid WorkspaceId { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public List<VariableDefinition> Variables { get; init; } = [];
}

public sealed record AppProfile
{
    public Guid? LastWorkspaceId { get; init; }

    public List<VariableDefinition> GlobalVariables { get; init; } = [];

    public ThemeKind Theme { get; init; } = ThemeKind.System;

    public PaneLayoutPreference PaneLayout { get; init; } = new();
}

public sealed record WorkspaceSnapshot
{
    public WorkspaceDefinition Workspace { get; init; } = new();

    public List<WorkspaceNodeDefinition> Nodes { get; init; } = [];

    public List<EnvironmentDefinition> Environments { get; init; } = [];

    public List<ExecutionPreset> ExecutionPresets { get; init; } = [];
}

public sealed record AppState
{
    public AppProfile Profile { get; init; } = new();

    public List<WorkspaceSnapshot> Workspaces { get; init; } = [];
}

public sealed record VariableSource
{
    public string Value { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<VariableScope>))]
    public VariableScope Scope { get; init; }
}

public sealed record ResolvedVariable
{
    public string Key { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<VariableScope>))]
    public VariableScope EffectiveScope { get; init; }

    public List<VariableSource> OverrideChain { get; init; } = [];

    public bool IsSecret { get; init; }
}

public sealed record VariableResolutionPreview
{
    public List<ResolvedVariable> Variables { get; init; } = [];

    public string RenderedText { get; init; } = string.Empty;
}
