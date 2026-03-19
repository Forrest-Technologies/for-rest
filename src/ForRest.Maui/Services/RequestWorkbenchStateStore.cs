using System.Text.Json;

namespace ForRest.Maui.Services;

public sealed class RequestWorkbenchStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string stateFilePath;

    public RequestWorkbenchStateStore()
    {
        stateFilePath = Path.Combine(FileSystem.AppDataDirectory, "request-workbench-state.json");
    }

    public async Task<RequestWorkbenchState> LoadAsync(
        IReadOnlyList<RequestWorkbenchDocumentState> defaults,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(stateFilePath))
        {
            return CreateDefaultState(defaults);
        }

        try
        {
            await using var stream = File.OpenRead(stateFilePath);
            var loaded = await JsonSerializer.DeserializeAsync<RequestWorkbenchState>(stream, SerializerOptions, cancellationToken);
            if (loaded is null)
            {
                return CreateDefaultState(defaults);
            }

            var mergedDocuments = defaults
                .Select(defaultDocument =>
                    loaded.Documents.FirstOrDefault(
                        document => string.Equals(document.Location, defaultDocument.Location, StringComparison.OrdinalIgnoreCase))
                    ?? defaultDocument)
                .ToList();

            return loaded with
            {
                Documents = mergedDocuments
            };
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

    private static RequestWorkbenchState CreateDefaultState(IReadOnlyList<RequestWorkbenchDocumentState> defaults)
    {
        return new()
        {
            SelectedDocumentLocation = defaults.FirstOrDefault()?.Location ?? string.Empty,
            Documents = [.. defaults]
        };
    }
}

public sealed record RequestWorkbenchState
{
    public string SelectedWorkspace { get; init; } = "for-rest://echo-lab";

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
