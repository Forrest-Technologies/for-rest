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
        if (!OperatingSystem.IsWindows())
        {
            // SqliteWorkspaceRepository round-trips secrets through DPAPI
            // (System.Security.Cryptography.ProtectedData), which only works
            // on Windows. The production repository is Windows-only too —
            // see the [SupportedOSPlatform("windows")] attribute on
            // SqliteWorkspaceRepository and the CA1416 analyzer hint it
            // emits elsewhere. Skip cleanly on macOS / Linux CI so the
            // test run stays green across platforms.
            Assert.Inconclusive("SqliteWorkspaceRepository + DPAPI secret protection is Windows-only.");
            return;
        }

        var database = new SqliteAppDatabase(NullLogger<SqliteAppDatabase>.Instance);
        var repository = new SqliteWorkspaceRepository(database, NullLogger<SqliteWorkspaceRepository>.Instance);
        var state = CreateState();

        await repository.Save(state);
        var loaded = await repository.Load();

        Assert.AreEqual("super-secret", loaded.Profile.GlobalVariables.Single(static item => item.Key == "apiToken").Value);
        Assert.AreEqual("db-password", loaded.Workspaces.Single().Nodes.Single(static item => item.Request is not null).Request!.Auth.Password);
        Assert.AreEqual(ThemeKind.System, loaded.Profile.Theme);
        Assert.AreEqual(ThemeKind.System, loaded.Workspaces.Single().Workspace.Theme);
        Assert.AreEqual(310, loaded.Workspaces.Single().Workspace.Settings.PaneLayout.LeftPaneWidth, 0.1);
        Assert.AreEqual(820, loaded.Workspaces.Single().Workspace.Settings.PaneLayout.MiddlePaneWidth, 0.1);
        Assert.AreEqual(460, loaded.Workspaces.Single().Workspace.Settings.PaneLayout.RightPaneWidth, 0.1);
        Assert.AreEqual(14, loaded.Workspaces.Single().Workspace.Settings.PaneLayout.EditorFontSize, 0.1);

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

    [TestMethod]
    public async Task Execution_history_repository_protects_secret_runtime_values()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Secret runtime variables round-trip through DPAPI, which is Windows-only —
            // the same constraint the workspace repository test documents above.
            Assert.Inconclusive("Execution history secret protection via DPAPI is Windows-only.");
            return;
        }

        var workspaceId = Guid.NewGuid();
        var run = new ExecutionRun
        {
            WorkspaceId = workspaceId,
            RequestId = Guid.NewGuid(),
            RequestName = "Login",
            RuntimeVariables =
            [
                new()
                {
                    Key = "accessToken",
                    Value = "runtime-token-value",
                    Scope = VariableScope.Runtime,
                    IsSecret = true,
                },
                new()
                {
                    Key = "plainValue",
                    Value = "visible",
                    Scope = VariableScope.Runtime,
                },
            ],
        };

        var database = new SqliteAppDatabase(NullLogger<SqliteAppDatabase>.Instance);
        var repository = new SqliteExecutionHistoryRepository(database, NullLogger<SqliteExecutionHistoryRepository>.Instance);

        await repository.Add(run);
        var loaded = await repository.Load(workspaceId);

        Assert.AreEqual("runtime-token-value", loaded.Single().RuntimeVariables.Single(static item => item.Key == "accessToken").Value);
        Assert.AreEqual("visible", loaded.Single().RuntimeVariables.Single(static item => item.Key == "plainValue").Value);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT run_json FROM execution_history;";
        var payload = command.ExecuteScalar()?.ToString() ?? string.Empty;

        Assert.IsFalse(payload.Contains("runtime-token-value", StringComparison.Ordinal));
        StringAssert.Contains(payload, "dpapi:");
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
                Theme = ThemeKind.System,
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
                        Theme = ThemeKind.System,
                        Settings = new()
                        {
                            PaneLayout = new()
                            {
                                LeftPaneWidth = 310,
                                MiddlePaneWidth = 820,
                                RightPaneWidth = 460,
                                EditorFontSize = 14,
                            },
                        },
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
