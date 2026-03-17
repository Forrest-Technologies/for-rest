namespace ForRest.Plugins.Abstractions;

public interface IForRestPlugin
{
    string Id { get; }

    string Name { get; }

    Version ApiVersion { get; }
}

public interface IAuthProviderPlugin : IForRestPlugin
{
    AuthMode Mode { get; }
}

public interface IResponseViewerPlugin : IForRestPlugin
{
    string SupportedContentType { get; }
}

public sealed record PluginManifest
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string AssemblyPath { get; init; } = string.Empty;

    public string EntryType { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = "1.0.0";

    public bool Enabled { get; init; } = true;
}
