namespace ForRest.Repositories;

public interface IWorkspaceRepository
{
    Task<AppState> Load(CancellationToken cancellationToken = default);

    Task Save(AppState state, CancellationToken cancellationToken = default);
}
