using ForRest.Repositories;

namespace ForRest.Infrastructure.Sqlite;

public sealed class SqliteExecutionHistoryRepository(SqliteAppDatabase database, ILogger<SqliteExecutionHistoryRepository> logger) : IExecutionHistoryRepository
{
    #region Public Methods

    public Task<List<ExecutionRun>> Load(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        database.Initialize();

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT run_json
            FROM execution_history
            WHERE workspace_id = $workspaceId
            ORDER BY started_utc DESC
            LIMIT 100;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId.ToString("D"));

        using var reader = command.ExecuteReader();
        var runs = new List<ExecutionRun>();
        while (reader.Read())
        {
            if (reader.GetString(0) is { Length: > 0 } payload)
            {
                runs.Add(JsonSerializer.Deserialize<ExecutionRun>(payload, database.JsonOptions) ?? new ExecutionRun());
            }
        }

        logger.LogInformation("Loaded {RunCount} execution history records for workspace {WorkspaceId}", runs.Count, workspaceId);
        return Task.FromResult(runs);
    }

    public Task Add(ExecutionRun run, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        database.Initialize();

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO execution_history(id, workspace_id, started_utc, run_json)
            VALUES ($id, $workspaceId, $startedUtc, $payload);
            """;
        command.Parameters.AddWithValue("$id", run.Id.ToString("D"));
        command.Parameters.AddWithValue("$workspaceId", run.WorkspaceId.ToString("D"));
        command.Parameters.AddWithValue("$startedUtc", run.StartedUtc.ToString("O"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(run, database.JsonOptions));
        command.ExecuteNonQuery();

        logger.LogInformation("Stored execution run {RunId} for request {RequestId}", run.Id, run.RequestId);
        return Task.CompletedTask;
    }

    #endregion
}
