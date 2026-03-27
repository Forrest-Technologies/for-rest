using Microsoft.Maui.Storage;

namespace ForRest.Maui.Theming;

public sealed class ThemeConfigStore
{
	private const string FileName = "settings.toml";
	private const string WorkspaceMarkerFile = "ForRest.slnx";
	private const string ConfigFileOverrideEnvironmentVariable = "FORREST_CONFIG_FILE";
	private readonly Lazy<string> _configFilePath;

	public ThemeConfigStore()
	{
		_configFilePath = new(ResolveConfigFilePath);
	}

	public string ConfigFilePath => _configFilePath.Value;

	public string ConfigDirectoryPath => Path.GetDirectoryName(ConfigFilePath)!;

	public void EnsureConfigFile(string initialContent)
	{
		Directory.CreateDirectory(ConfigDirectoryPath);

		if (!File.Exists(ConfigFilePath))
		{
			File.WriteAllText(ConfigFilePath, NormalizeLineEndings(initialContent));
		}
	}

	public string ReadAllText()
	{
		return File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
	}

	public void WriteAllText(string content)
	{
		Directory.CreateDirectory(ConfigDirectoryPath);
		File.WriteAllText(ConfigFilePath, NormalizeLineEndings(content));
	}

	public static string NormalizeLineEndings(string text)
	{
		return text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
	}

	private static string ResolveConfigFilePath()
	{
		string? explicitPath = Environment.GetEnvironmentVariable(ConfigFileOverrideEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(explicitPath))
		{
			return Path.GetFullPath(explicitPath);
		}

		string? workspaceRoot =
			TryFindWorkspaceRoot(AppContext.BaseDirectory) ??
			TryFindWorkspaceRoot(Environment.CurrentDirectory);

		if (!string.IsNullOrWhiteSpace(workspaceRoot))
		{
			return Path.Combine(workspaceRoot, "config", FileName);
		}

		foreach (string candidateDirectory in GetFallbackConfigDirectories())
		{
			if (!string.IsNullOrWhiteSpace(candidateDirectory))
			{
				return Path.Combine(candidateDirectory, "config", FileName);
			}
		}

		return Path.Combine(Path.GetTempPath(), "ForRest", "config", FileName);
	}

	private static IEnumerable<string> GetFallbackConfigDirectories()
	{
		string? currentAppDataDirectory = TryResolvePath(static () => FileSystem.Current.AppDataDirectory);
		if (!string.IsNullOrWhiteSpace(currentAppDataDirectory))
		{
			yield return currentAppDataDirectory;
		}

		string? legacyAppDataDirectory = TryResolvePath(static () => FileSystem.AppDataDirectory);
		if (!string.IsNullOrWhiteSpace(legacyAppDataDirectory) &&
		    !string.Equals(legacyAppDataDirectory, currentAppDataDirectory, StringComparison.OrdinalIgnoreCase))
		{
			yield return legacyAppDataDirectory;
		}

		string? localAppData = TryResolvePath(static () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
		if (!string.IsNullOrWhiteSpace(localAppData))
		{
			yield return Path.Combine(localAppData, "ForRest");
		}

		yield return Path.Combine(Path.GetTempPath(), "ForRest");
	}

	private static string? TryFindWorkspaceRoot(string? startPath)
	{
		if (string.IsNullOrWhiteSpace(startPath))
		{
			return null;
		}

		string fullPath = Path.GetFullPath(startPath);
		DirectoryInfo? directory = Directory.Exists(fullPath)
			? new DirectoryInfo(fullPath)
			: new FileInfo(fullPath).Directory;

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, WorkspaceMarkerFile)) ||
			    Directory.Exists(Path.Combine(directory.FullName, ".git")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private static string? TryResolvePath(Func<string> resolver)
	{
		try
		{
			return resolver();
		}
		catch
		{
			return null;
		}
	}
}
