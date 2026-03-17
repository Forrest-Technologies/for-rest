namespace ForRest.Services;

public sealed class WorkspaceService(IWorkspaceRepository workspaceRepository, ILogger<WorkspaceService> logger) : IWorkspaceService
{
    #region Public Methods

    public async Task<AppState> Load(CancellationToken cancellationToken = default)
    {
        var state = await workspaceRepository.Load(cancellationToken);
        if (state.Workspaces.Count > 0)
        {
            return state;
        }

        var seededState = SampleDataFactory.Create();
        await workspaceRepository.Save(seededState, cancellationToken);
        logger.LogInformation("Seeded initial For-Rest app state");
        return seededState;
    }

    public Task Save(AppState state, CancellationToken cancellationToken = default)
    {
        return workspaceRepository.Save(state, cancellationToken);
    }

    #endregion
}
