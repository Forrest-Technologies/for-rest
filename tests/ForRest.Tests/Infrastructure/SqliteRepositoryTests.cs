using System.Text.Json;

namespace ForRest.Tests.Infrastructure;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class SqliteRepositoryTests
{
    #region Private Fields

    private string originalDataDirectory = string.Empty;
    private string tempDataDirectory = string.Empty;

    #endregion

    #region Public Methods

    [TestInitialize]
    public void Initialize()
    {
        originalDataDirectory = Environment.GetEnvironmentVariable("FORREST_DATA_DIR") ?? string.Empty;
        tempDataDirectory = Path.Combine(Path.GetTempPath(), "ForRest.Tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("FORREST_DATA_DIR", tempDataDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("FORREST_DATA_DIR", string.IsNullOrWhiteSpace(originalDataDirectory) ? null : originalDataDirectory);

        if (Directory.Exists(tempDataDirectory))
        {
            TryDeleteTempDirectory();
        }
    }

    [TestMethod]
    public async Task Workspace_repository_round_trips_state_and_protects_secret_values()
    {
        var database = new SqliteAppDatabase(NullLogger<SqliteAppDatabase>.Instance);
        var repository = new SqliteWorkspaceRepository(database, NullLogger<SqliteWorkspaceRepository>.Instance);
        var state = CreateState();

        await repository.Save(state);
        var loaded = await repository.Load();

        Assert.AreEqual("super-secret", loaded.Profile.GlobalVariables.Single(static item => item.Key == "apiToken").Value);
        Assert.AreEqual("db-password", loaded.Workspaces.Single().Nodes.Single(static item => item.Request is not null).Request!.Auth.Password);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM app_state WHERE id = 1;";
        var payload = command.ExecuteScalar()?.ToString() ?? string.Empty;

        Assert.IsFalse(payload.Contains("super-secret", StringComparison.Ordinal));
        Assert.IsFalse(payload.Contains("db-password", StringComparison.Ordinal));
        StringAssert.Contains(payload, "dpapi:");

        var persisted = JsonSerializer.Deserialize<AppState>(payload, database.JsonOptions);
        Assert.IsNotNull(persisted);
    }

    [TestMethod]
    public async Task Execution_history_repository_stores_and_loads_runs()
    {
        var workspaceId = Guid.NewGuid();
        var run = new ExecutionRun
        {
            WorkspaceId = workspaceId,
            RequestId = Guid.NewGuid(),
            RequestName = "Get Users",
            Response = new()
            {
                StatusCode = 200,
                Body = """{"ok":true}""",
            },
        };

        var database = new SqliteAppDatabase(NullLogger<SqliteAppDatabase>.Instance);
        var repository = new SqliteExecutionHistoryRepository(database, NullLogger<SqliteExecutionHistoryRepository>.Instance);

        await repository.Add(run);
        var loaded = await repository.Load(workspaceId);

        Assert.HasCount(1, loaded);
        Assert.AreEqual("Get Users", loaded.Single().RequestName);
        Assert.AreEqual(200, loaded.Single().Response?.StatusCode);
    }

    #endregion

    #region Private Methods

    private static AppState CreateState()
    {
        var workspaceId = Guid.NewGuid();

        return new()
        {
            Profile = new()
            {
                GlobalVariables =
                [
                    new()
                    {
                        Key = "apiToken",
                        Value = "super-secret",
                        Scope = VariableScope.Global,
                        IsSecret = true,
                    },
                ],
            },
            Workspaces =
            [
                new()
                {
                    Workspace = new()
                    {
                        Id = workspaceId,
                        Name = "Demo",
                    },
                    Nodes =
                    [
                        new()
                        {
                            WorkspaceId = workspaceId,
                            Kind = WorkspaceNodeKind.Request,
                            Name = "Auth Request",
                            Request = new()
                            {
                                WorkspaceId = workspaceId,
                                Name = "Auth Request",
                                UrlTemplate = "https://api.example.test",
                                Auth = new()
                                {
                                    Mode = AuthMode.Basic,
                                    Username = "alice",
                                    Password = "db-password",
                                },
                                Variables =
                                [
                                    new()
                                    {
                                        Key = "localSecret",
                                        Value = "value",
                                        Scope = VariableScope.RequestLocal,
                                        IsSecret = true,
                                    },
                                ],
                            },
                        },
                    ],
                },
            ],
        };
    }

    private void TryDeleteTempDirectory()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(tempDataDirectory, recursive: true);
                return;
            }
            catch (IOException)
            {
                if (attempt == 4)
                {
                    break;
                }

                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt == 4)
                {
                    break;
                }

                Thread.Sleep(50);
            }
        }

        try
        {
            var fallbackDirectory = Path.Combine(Path.GetTempPath(), "ForRest.Tests.Orphaned");
            Directory.CreateDirectory(fallbackDirectory);
            var targetDirectory = Path.Combine(fallbackDirectory, Path.GetFileName(tempDataDirectory));
            if (!Directory.Exists(targetDirectory))
            {
                Directory.Move(tempDataDirectory, targetDirectory);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    #endregion
}
