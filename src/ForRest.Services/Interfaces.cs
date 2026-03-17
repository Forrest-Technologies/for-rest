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
}

public interface IRepeatRunnerService
{
    Task<List<T>> Run<T>(
        ScheduleDefinition schedule,
        Func<int, CancellationToken, Task<T>> iteration,
        CancellationToken cancellationToken = default);
}
