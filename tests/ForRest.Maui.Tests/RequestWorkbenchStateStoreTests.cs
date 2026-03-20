using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ForRest.Maui.Services;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class RequestWorkbenchStateStoreTests
{
	private string _stateFilePath = string.Empty;

	[TestInitialize]
	public void Initialize()
	{
		string testDirectory = Path.Combine(Path.GetTempPath(), "for-rest-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(testDirectory);
		_stateFilePath = Path.Combine(testDirectory, "request-workbench-state.json");
	}

	[TestCleanup]
	public void Cleanup()
	{
		if (string.IsNullOrWhiteSpace(_stateFilePath))
		{
			return;
		}

		string? directory = Path.GetDirectoryName(_stateFilePath);
		if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[TestMethod]
	public async Task LoadAsync_returns_workspace_defaults_when_file_is_missing()
	{
		RequestWorkbenchStateStore store = new(_stateFilePath);

		RequestWorkbenchState state = await store.LoadAsync(BuildDefaults());

		Assert.HasCount(2, state.Workspaces);
		Assert.AreEqual(BuildDefaults()[0].Id, state.SelectedWorkspaceId);
		Assert.AreEqual("HTTP Bin Playground", state.Workspaces[0].Name);
	}

	[TestMethod]
	public async Task LoadAsync_migrates_legacy_flat_state_into_first_workspace()
	{
		var legacyState = new
		{
			selectedWorkspace = "Legacy Playground",
			selectedEnvironment = "Stage",
			selectedDocumentLocation = "/requests/httpbin/headers",
			documents = new[]
			{
				new
				{
					title = "Legacy Headers",
					method = "GET",
					summary = "Migrated summary",
					location = "/requests/httpbin/headers",
					requestSource = "request {}",
					preRequestScript = "console.Log(\"legacy\");"
				}
			}
		};

		await File.WriteAllTextAsync(
			_stateFilePath,
			JsonSerializer.Serialize(
				legacyState,
				new JsonSerializerOptions
				{
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
					WriteIndented = true
				}));

		RequestWorkbenchStateStore store = new(_stateFilePath);
		RequestWorkbenchState state = await store.LoadAsync(BuildDefaults());

		Assert.HasCount(2, state.Workspaces);
		Assert.AreEqual("Legacy Playground", state.Workspaces[0].Name);
		Assert.AreEqual("Stage", state.Workspaces[0].SelectedEnvironment);
		Assert.AreEqual("Legacy Headers", state.Workspaces[0].Documents[0].Title);
		Assert.AreEqual(state.Workspaces[0].Id, state.SelectedWorkspaceId);
	}

	[TestMethod]
	public async Task SaveAsync_and_LoadAsync_preserve_user_added_workspace()
	{
		RequestWorkbenchStateStore store = new(_stateFilePath);
		List<RequestWorkbenchWorkspaceState> defaults = BuildDefaults();
		RequestWorkbenchWorkspaceState userWorkspace = new()
		{
			Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
			Name = "Workspace 3",
			SelectedEnvironment = "Local",
			SelectedDocumentLocation = "/requests/custom/one",
			Documents =
			[
				new()
				{
					Title = "Custom Request",
					Method = "GET",
					Summary = "User-added request",
					Location = "/requests/custom/one",
					RequestSource = "request {}",
					PreRequestScript = string.Empty
				}
			]
		};

		await store.SaveAsync(
			new()
			{
				SelectedWorkspaceId = userWorkspace.Id,
				Workspaces = [.. defaults, userWorkspace]
			});

		RequestWorkbenchState loaded = await store.LoadAsync(defaults);

		Assert.HasCount(3, loaded.Workspaces);
		Assert.AreEqual(userWorkspace.Id, loaded.SelectedWorkspaceId);
		Assert.IsTrue(loaded.Workspaces.Any(workspace => workspace.Id == userWorkspace.Id && workspace.Name == "Workspace 3"));
	}

	private static List<RequestWorkbenchWorkspaceState> BuildDefaults()
	{
		return
		[
			new()
			{
				Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
				Name = "HTTP Bin Playground",
				SelectedEnvironment = "Local",
				SelectedDocumentLocation = "/requests/httpbin/headers",
				Documents =
				[
					new()
					{
						Title = "Get Headers",
						Method = "GET",
						Summary = "Inspect request headers round-trip",
						Location = "/requests/httpbin/headers",
						RequestSource = "request {}",
						PreRequestScript = string.Empty
					}
				]
			},
			new()
			{
				Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
				Name = "JSON Placeholder",
				SelectedEnvironment = "Local",
				SelectedDocumentLocation = "/requests/jsonplaceholder/todo",
				Documents =
				[
					new()
					{
						Title = "Get Todo",
						Method = "GET",
						Summary = "Read a simple todo resource",
						Location = "/requests/jsonplaceholder/todo",
						RequestSource = "request {}",
						PreRequestScript = string.Empty
					}
				]
			}
		];
	}
}
