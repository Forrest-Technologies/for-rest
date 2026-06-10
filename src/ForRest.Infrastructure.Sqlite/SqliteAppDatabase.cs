namespace ForRest.Infrastructure.Sqlite;

public sealed class SqliteAppDatabase(ILogger<SqliteAppDatabase> logger)
{
    #region Private Fields

    private const string CreateAppStateTableSql = """
        CREATE TABLE IF NOT EXISTS app_state (
            id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
            payload_json TEXT NOT NULL
        );
        """;

    private const string CreateExecutionHistoryTableSql = """
        CREATE TABLE IF NOT EXISTS execution_history (
            id TEXT NOT NULL PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            started_utc TEXT NOT NULL,
            run_json TEXT NOT NULL
        );
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string databasePath = ResolveDatabasePath();

    #endregion

    #region Properties

    public string DatabasePath => databasePath;

    public JsonSerializerOptions JsonOptions => SerializerOptions;

    #endregion

    #region Public Methods

    public void Initialize()
    {
        var directoryPath = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"{CreateAppStateTableSql}{Environment.NewLine}{CreateExecutionHistoryTableSql}";
        command.ExecuteNonQuery();

        logger.LogInformation("Initialized For-Rest SQLite database at {DatabasePath}", databasePath);
    }

    public SqliteConnection OpenConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ConnectionString;
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    #endregion

    #region Private Methods

    private static string ResolveDatabasePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("FORREST_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.Combine(overridePath, "forrest.db");
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "ForRest", "forrest.db");
    }

    #endregion
}
