namespace ForRest.Services;

public interface IWorkspaceService
{
    Task<AppState> Load(CancellationToken cancellationToken = default);

    Task Save(AppState state, CancellationToken cancellationToken = default);
}

public interface IRequestExecutionService
{
    Task<RequestExecutionResult> Execute(
        AppProfile profile,
        WorkspaceSnapshot workspace,
        RequestDefinition request,
        EnvironmentDefinition? environment,
        CancellationToken cancellationToken = default);

    Task<RequestExecutionResult> Execute(
        AppProfile profile,
        WorkspaceSnapshot workspace,
        RequestDefinition request,
        EnvironmentDefinition? environment,
        IReadOnlyList<VariableDefinition> initialRuntimeVariables,
        CancellationToken cancellationToken = default);
}

public interface IRepeatRunnerService
{
    Task<List<T>> Run<T>(
        ScheduleDefinition schedule,
        Func<int, CancellationToken, Task<T>> iteration,
        CancellationToken cancellationToken = default);
}

public interface IForRestScriptExecutionService
{
    ForRestScriptCompilationResult Compile(string source, Guid workspaceId, string? defaultRequestName = null);

    Task<ForRestScriptExecutionOutcome> Execute(
        AppProfile profile,
        WorkspaceSnapshot workspace,
        string source,
        EnvironmentDefinition? environment,
        string? defaultRequestName = null,
        string? preRequestScriptOverride = null,
        CancellationToken cancellationToken = default);
}

public sealed record ForRestScriptExecutionOutcome
{
    public ForRestScriptCompilationResult Compilation { get; init; } = new(null, null, []);

    public RequestExecutionResult? Execution { get; init; }
}
