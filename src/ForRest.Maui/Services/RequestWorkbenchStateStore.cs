using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace ForRest.Maui.Services;

public sealed class RequestWorkbenchStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string stateFilePath;

    public RequestWorkbenchStateStore(string? customStateFilePath = null)
    {
        stateFilePath = string.IsNullOrWhiteSpace(customStateFilePath)
            ? Path.Combine(FileSystem.AppDataDirectory, "request-workbench-state.json")
            : customStateFilePath;
    }

    public async Task<RequestWorkbenchState> LoadAsync(
        IReadOnlyList<RequestWorkbenchWorkspaceState> defaults,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(stateFilePath))
        {
            return CreateDefaultState(defaults);
        }

        try
        {
            string json = await File.ReadAllTextAsync(stateFilePath, cancellationToken);
            var loaded = JsonSerializer.Deserialize<RequestWorkbenchState>(json, SerializerOptions);
            if (loaded is not null && loaded.Workspaces.Count > 0)
            {
                return MergeWithDefaults(loaded, defaults);
            }

            var legacy = JsonSerializer.Deserialize<LegacyRequestWorkbenchState>(json, SerializerOptions);
            if (legacy is not null)
            {
                return ConvertLegacyState(legacy, defaults);
            }

            return CreateDefaultState(defaults);
        }
        catch
        {
            return CreateDefaultState(defaults);
        }
    }

    public async Task SaveAsync(RequestWorkbenchState state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stateFilePath)!);
        await using var stream = File.Create(stateFilePath);
        await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken);
    }

    private static RequestWorkbenchState CreateDefaultState(IReadOnlyList<RequestWorkbenchWorkspaceState> defaults)
    {
        List<RequestWorkbenchWorkspaceState> workspaces = defaults
            .Select(NormalizeWorkspace)
            .ToList();

        return new()
        {
            SelectedWorkspaceId = workspaces.FirstOrDefault()?.Id ?? Guid.Empty,
            Workspaces = workspaces
        };
    }

    private static RequestWorkbenchState MergeWithDefaults(
        RequestWorkbenchState loaded,
        IReadOnlyList<RequestWorkbenchWorkspaceState> defaults)
    {
        Dictionary<Guid, RequestWorkbenchWorkspaceState> defaultMap = defaults
            .Select(NormalizeWorkspace)
            .ToDictionary(static item => item.Id);
        List<RequestWorkbenchWorkspaceState> merged = [];

        foreach (RequestWorkbenchWorkspaceState workspace in loaded.Workspaces.Select(NormalizeWorkspace))
        {
            if (defaultMap.Remove(workspace.Id, out RequestWorkbenchWorkspaceState? defaultWorkspace))
            {
                merged.Add(MergeWorkspace(defaultWorkspace, workspace));
                continue;
            }

            merged.Add(workspace);
        }

        merged.AddRange(defaultMap.Values);

        Guid selectedWorkspaceId = merged.Any(item => item.Id == loaded.SelectedWorkspaceId)
            ? loaded.SelectedWorkspaceId
            : merged.FirstOrDefault()?.Id ?? Guid.Empty;

        return new()
        {
            SelectedWorkspaceId = selectedWorkspaceId,
            Workspaces = merged
        };
    }

    private static RequestWorkbenchState ConvertLegacyState(
        LegacyRequestWorkbenchState legacy,
        IReadOnlyList<RequestWorkbenchWorkspaceState> defaults)
    {
        RequestWorkbenchState defaultState = CreateDefaultState(defaults);
        RequestWorkbenchWorkspaceState seedWorkspace = defaultState.Workspaces.FirstOrDefault()
            ?? new RequestWorkbenchWorkspaceState();

        RequestWorkbenchWorkspaceState migratedWorkspace = seedWorkspace with
        {
            Name = string.IsNullOrWhiteSpace(legacy.SelectedWorkspace) ? seedWorkspace.Name : legacy.SelectedWorkspace,
            SelectedEnvironment = string.IsNullOrWhiteSpace(legacy.SelectedEnvironment) ? seedWorkspace.SelectedEnvironment : legacy.SelectedEnvironment,
            SelectedDocumentLocation = string.IsNullOrWhiteSpace(legacy.SelectedDocumentLocation)
                ? seedWorkspace.SelectedDocumentLocation
                : legacy.SelectedDocumentLocation,
            Documents = MergeDocuments(seedWorkspace.Documents, legacy.Documents)
        };

        List<RequestWorkbenchWorkspaceState> workspaces =
        [
            migratedWorkspace,
            .. defaultState.Workspaces.Where(item => item.Id != migratedWorkspace.Id)
        ];

        return new()
        {
            SelectedWorkspaceId = migratedWorkspace.Id,
            Workspaces = workspaces
        };
    }

    private static RequestWorkbenchWorkspaceState MergeWorkspace(
        RequestWorkbenchWorkspaceState defaults,
        RequestWorkbenchWorkspaceState loaded)
    {
        List<RequestWorkbenchDocumentState> documents = MergeDocuments(defaults.Documents, loaded.Documents);
        string selectedDocumentLocation = documents.Any(
            document => string.Equals(document.Location, loaded.SelectedDocumentLocation, StringComparison.OrdinalIgnoreCase))
            ? loaded.SelectedDocumentLocation
            : documents.FirstOrDefault()?.Location ?? string.Empty;

        return defaults with
        {
            Name = string.IsNullOrWhiteSpace(loaded.Name) ? defaults.Name : loaded.Name,
            SelectedEnvironment = string.IsNullOrWhiteSpace(loaded.SelectedEnvironment) ? defaults.SelectedEnvironment : loaded.SelectedEnvironment,
            SelectedDocumentLocation = selectedDocumentLocation,
            Documents = documents
        };
    }

    private static List<RequestWorkbenchDocumentState> MergeDocuments(
        IReadOnlyList<RequestWorkbenchDocumentState> defaults,
        IReadOnlyList<RequestWorkbenchDocumentState> loaded)
    {
        Dictionary<string, RequestWorkbenchDocumentState> loadedMap = loaded
            .Where(static item => !string.IsNullOrWhiteSpace(item.Location))
            .ToDictionary(static item => item.Location, StringComparer.OrdinalIgnoreCase);
        List<RequestWorkbenchDocumentState> merged = [];

        foreach (RequestWorkbenchDocumentState defaultDocument in defaults)
        {
            if (loadedMap.Remove(defaultDocument.Location, out RequestWorkbenchDocumentState? existing))
            {
                merged.Add(existing);
                continue;
            }

            merged.Add(defaultDocument);
        }

        merged.AddRange(loadedMap.Values);
        return merged;
    }

    private static RequestWorkbenchWorkspaceState NormalizeWorkspace(RequestWorkbenchWorkspaceState workspace)
    {
        List<RequestWorkbenchDocumentState> documents = workspace.Documents
            .Where(static item => !string.IsNullOrWhiteSpace(item.Location))
            .ToList();
        string selectedDocumentLocation = documents.Any(
            document => string.Equals(document.Location, workspace.SelectedDocumentLocation, StringComparison.OrdinalIgnoreCase))
            ? workspace.SelectedDocumentLocation
            : documents.FirstOrDefault()?.Location ?? string.Empty;

        return workspace with
        {
            Id = workspace.Id == Guid.Empty ? Guid.NewGuid() : workspace.Id,
            Name = string.IsNullOrWhiteSpace(workspace.Name) ? "Workspace" : workspace.Name,
            SelectedEnvironment = string.IsNullOrWhiteSpace(workspace.SelectedEnvironment) ? "Local" : workspace.SelectedEnvironment,
            SelectedDocumentLocation = selectedDocumentLocation,
            Documents = documents
        };
    }
}

public sealed record RequestWorkbenchState
{
    public Guid SelectedWorkspaceId { get; init; }

    public List<RequestWorkbenchWorkspaceState> Workspaces { get; init; } = [];
}

public sealed record RequestWorkbenchWorkspaceState
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = "Workspace";

    public string SelectedEnvironment { get; init; } = "Local";

    public string SelectedDocumentLocation { get; init; } = string.Empty;

    public List<RequestWorkbenchDocumentState> Documents { get; init; } = [];
}

public sealed record RequestWorkbenchDocumentState
{
    public string Title { get; init; } = string.Empty;

    public string Method { get; init; } = "GET";

    public string Summary { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string RequestSource { get; init; } = string.Empty;

    public string PreRequestScript { get; init; } = string.Empty;
}

internal sealed record LegacyRequestWorkbenchState
{
    public string SelectedWorkspace { get; init; } = string.Empty;

    public string SelectedEnvironment { get; init; } = "Local";

    public string SelectedDocumentLocation { get; init; } = string.Empty;

    public List<RequestWorkbenchDocumentState> Documents { get; init; } = [];
}
