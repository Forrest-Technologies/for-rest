using ForRest.Models;
using ForRest.Repositories;

namespace ForRest.Maui.Services;

public sealed class InMemoryExecutionHistoryRepository : IExecutionHistoryRepository
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, List<ExecutionRun>> runsByWorkspace = [];

    public Task<List<ExecutionRun>> Load(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            if (!runsByWorkspace.TryGetValue(workspaceId, out var runs))
            {
                return Task.FromResult(new List<ExecutionRun>());
            }

            return Task.FromResult(runs.OrderByDescending(static item => item.StartedUtc).ToList());
        }
    }

    public Task Add(ExecutionRun run, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            if (!runsByWorkspace.TryGetValue(run.WorkspaceId, out var runs))
            {
                runs = [];
                runsByWorkspace[run.WorkspaceId] = runs;
            }

            runs.Add(run);
        }

        return Task.CompletedTask;
    }
}
