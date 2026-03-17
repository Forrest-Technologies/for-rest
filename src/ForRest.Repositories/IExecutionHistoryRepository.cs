namespace ForRest.Repositories;

public interface IExecutionHistoryRepository
{
    Task<List<ExecutionRun>> Load(Guid workspaceId, CancellationToken cancellationToken = default);

    Task Add(ExecutionRun run, CancellationToken cancellationToken = default);
}
