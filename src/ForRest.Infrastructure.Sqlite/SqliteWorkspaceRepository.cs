using ForRest.Repositories;

namespace ForRest.Infrastructure.Sqlite;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class SqliteWorkspaceRepository(SqliteAppDatabase database, ILogger<SqliteWorkspaceRepository> logger) : IWorkspaceRepository
{
    #region Public Methods

    public Task<AppState> Load(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        database.Initialize();

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM app_state WHERE id = 1;";

        var payload = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(payload))
        {
            logger.LogInformation("No stored app state found. Returning empty app state.");
            return Task.FromResult(new AppState());
        }

        var state = JsonSerializer.Deserialize<AppState>(payload, database.JsonOptions) ?? new AppState();
        return Task.FromResult(Unprotect(state));
    }

    public Task Save(AppState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        database.Initialize();

        var protectedState = Protect(state);
        var payload = JsonSerializer.Serialize(protectedState, database.JsonOptions);

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO app_state(id, payload_json)
            VALUES (1, $payload)
            ON CONFLICT(id) DO UPDATE SET
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$payload", payload);
        command.ExecuteNonQuery();
        transaction.Commit();

        logger.LogInformation("Persisted app state with {WorkspaceCount} workspaces", state.Workspaces.Count);
        return Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    private static AppState Protect(AppState state)
    {
        return state with
        {
            Profile = state.Profile with
            {
                GlobalVariables = [.. state.Profile.GlobalVariables.Select(ProtectVariable)],
            },
            Workspaces = [.. state.Workspaces.Select(ProtectWorkspace)],
        };
    }

    private static WorkspaceSnapshot ProtectWorkspace(WorkspaceSnapshot snapshot)
    {
        return snapshot with
        {
            Workspace = snapshot.Workspace with
            {
                Variables = [.. snapshot.Workspace.Variables.Select(ProtectVariable)],
            },
            Environments = [.. snapshot.Environments.Select(
                environment => environment with
                {
                    Variables = [.. environment.Variables.Select(ProtectVariable)],
                })],
            Nodes = [.. snapshot.Nodes.Select(
                node => node with
                {
                    Request = node.Request is null ? null : ProtectRequest(node.Request),
                })],
        };
    }

    private static RequestDefinition ProtectRequest(RequestDefinition request)
    {
        return request with
        {
            Variables = [.. request.Variables.Select(ProtectVariable)],
            Auth = request.Auth with
            {
                Password = SecretProtector.Protect(request.Auth.Password),
                BearerToken = SecretProtector.Protect(request.Auth.BearerToken),
                ApiKeyValue = SecretProtector.Protect(request.Auth.ApiKeyValue),
            },
        };
    }

    private static VariableDefinition ProtectVariable(VariableDefinition variable)
    {
        if (!variable.IsSecret)
        {
            return variable;
        }

        return variable with
        {
            Value = SecretProtector.Protect(variable.Value),
        };
    }

    private static AppState Unprotect(AppState state)
    {
        return state with
        {
            Profile = state.Profile with
            {
                GlobalVariables = [.. state.Profile.GlobalVariables.Select(UnprotectVariable)],
            },
            Workspaces = [.. state.Workspaces.Select(UnprotectWorkspace)],
        };
    }

    private static WorkspaceSnapshot UnprotectWorkspace(WorkspaceSnapshot snapshot)
    {
        return snapshot with
        {
            Workspace = snapshot.Workspace with
            {
                Variables = [.. snapshot.Workspace.Variables.Select(UnprotectVariable)],
            },
            Environments = [.. snapshot.Environments.Select(
                environment => environment with
                {
                    Variables = [.. environment.Variables.Select(UnprotectVariable)],
                })],
            Nodes = [.. snapshot.Nodes.Select(
                node => node with
                {
                    Request = node.Request is null ? null : UnprotectRequest(node.Request),
                })],
        };
    }

    private static RequestDefinition UnprotectRequest(RequestDefinition request)
    {
        return request with
        {
            Variables = [.. request.Variables.Select(UnprotectVariable)],
            Auth = request.Auth with
            {
                Password = SecretProtector.Unprotect(request.Auth.Password),
                BearerToken = SecretProtector.Unprotect(request.Auth.BearerToken),
                ApiKeyValue = SecretProtector.Unprotect(request.Auth.ApiKeyValue),
            },
        };
    }

    private static VariableDefinition UnprotectVariable(VariableDefinition variable)
    {
        if (!variable.IsSecret)
        {
            return variable;
        }

        return variable with
        {
            Value = SecretProtector.Unprotect(variable.Value),
        };
    }

    #endregion
}
