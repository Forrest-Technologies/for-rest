namespace ForRest.Plugins.Host;

public sealed class PluginCatalog(ILogger<PluginCatalog> logger)
{
    #region Public Methods

    public IReadOnlyList<PluginManifest> Discover(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            logger.LogInformation("Plugin directory {RootPath} does not exist yet", rootPath);
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(rootPath, "plugin.json", SearchOption.AllDirectories)
                .Select(TryReadManifest)
                .Where(static manifest => manifest is not null)
                .Select(static manifest => manifest!),
        ];
    }

    public IReadOnlyList<IForRestPlugin> LoadEnabled(IEnumerable<PluginManifest> manifests)
    {
        var plugins = new List<IForRestPlugin>();

        foreach (var manifest in manifests.Where(static item => item.Enabled))
        {
            try
            {
                var assembly = Assembly.LoadFrom(manifest.AssemblyPath);
                var type = assembly.GetType(manifest.EntryType, throwOnError: true);
                if (Activator.CreateInstance(type!) is IForRestPlugin plugin)
                {
                    plugins.Add(plugin);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to load plugin {PluginId}", manifest.Id);
            }
        }

        return plugins;
    }

    #endregion

    #region Private Methods

    private PluginManifest? TryReadManifest(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to read plugin manifest at {ManifestPath}", path);
            return null;
        }
    }

    #endregion
}
