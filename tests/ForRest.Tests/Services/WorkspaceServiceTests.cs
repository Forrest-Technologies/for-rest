namespace ForRest.Tests.Services;

[TestClass]
public sealed class WorkspaceServiceTests
{
    #region Public Methods

    [TestMethod]
    public async Task Load_seeds_initial_state_when_repository_is_empty()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new WorkspaceService(repository, NullLogger<WorkspaceService>.Instance);

        var state = await service.Load();

        Assert.IsNotEmpty(state.Workspaces);
        Assert.IsNotNull(repository.SavedState);
    }

    [TestMethod]
    public async Task Save_passes_state_to_repository()
    {
        var repository = new InMemoryWorkspaceRepository();
        var service = new WorkspaceService(repository, NullLogger<WorkspaceService>.Instance);
        var state = new AppState
        {
            Workspaces =
            [
                new()
                {
                    Workspace = new()
                    {
                        Name = "Demo",
                    },
                },
            ],
        };

        await service.Save(state);

        Assert.AreSame(state, repository.SavedState);
    }

    #endregion

    #region Helpers

    private sealed class InMemoryWorkspaceRepository : IWorkspaceRepository
    {
        public AppState CurrentState { get; set; } = new();

        public AppState? SavedState { get; private set; }

        public Task<AppState> Load(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CurrentState);
        }

        public Task Save(AppState state, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedState = state;
            CurrentState = state;
            return Task.CompletedTask;
        }
    }

    #endregion
}
