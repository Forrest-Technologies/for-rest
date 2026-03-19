using Microsoft.Maui.ApplicationModel;

namespace ForRest.Maui.Theming;

public sealed class ThemeService : IThemeService, IDisposable
{
	private readonly ThemeCatalog _themeCatalog;
	private readonly ThemeConfigParser _themeConfigParser;
	private readonly ThemeConfigNormalizer _themeConfigNormalizer;
	private readonly ThemeConfigStore _themeConfigStore;
	private readonly SettingsTomlTemplate _settingsTomlTemplate;
	private readonly SemaphoreSlim _processingLock = new(1, 1);
	private FileSystemWatcher? _watcher;
	private CancellationTokenSource? _reloadDebounceSource;
	private bool _started;

	public ThemeService(
		ThemeCatalog themeCatalog,
		ThemeConfigParser themeConfigParser,
		ThemeConfigNormalizer themeConfigNormalizer,
		ThemeConfigStore themeConfigStore,
		SettingsTomlTemplate settingsTomlTemplate)
	{
		_themeCatalog = themeCatalog;
		_themeConfigParser = themeConfigParser;
		_themeConfigNormalizer = themeConfigNormalizer;
		_themeConfigStore = themeConfigStore;
		_settingsTomlTemplate = settingsTomlTemplate;
		CurrentTheme = _themeCatalog.GetTheme(ShellThemeName.Azure);
		CurrentStatusMessage = "Ready";
	}

	public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

	public ShellThemeDefinition CurrentTheme { get; private set; }

	public string CurrentStatusMessage { get; private set; }

	public string ConfigFilePath => _themeConfigStore.ConfigFilePath;

	public void Start()
	{
		if (_started)
		{
			return;
		}

		_started = true;
		string initialConfig = _settingsTomlTemplate.Build(new ForRestSettings(ShellThemeName.Azure));

		_themeConfigStore.EnsureConfigFile(initialConfig);
		ProcessConfigCore("startup");
		StartWatcher();
	}

	public void Dispose()
	{
		_reloadDebounceSource?.Cancel();
		_reloadDebounceSource?.Dispose();
		_watcher?.Dispose();
		_processingLock.Dispose();
	}

	private void StartWatcher()
	{
		_watcher = new FileSystemWatcher(_themeConfigStore.ConfigDirectoryPath, Path.GetFileName(ConfigFilePath))
		{
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
			EnableRaisingEvents = true
		};

		_watcher.Changed += OnConfigFileChanged;
		_watcher.Created += OnConfigFileChanged;
		_watcher.Renamed += OnConfigFileChanged;
	}

	private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
	{
		CancellationTokenSource debounceSource = new();
		CancellationTokenSource? previousSource = Interlocked.Exchange(ref _reloadDebounceSource, debounceSource);
		previousSource?.Cancel();
		previousSource?.Dispose();

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(140, debounceSource.Token);
				await ProcessConfigAsync("config changed");
			}
			catch (OperationCanceledException)
			{
			}
		});
	}

	private async Task ProcessConfigAsync(string origin)
	{
		await _processingLock.WaitAsync();
		try
		{
			ProcessConfigCore(origin);
		}
		finally
		{
			_processingLock.Release();
		}
	}

	private void ProcessConfigCore(string origin)
	{
		string rawText = _themeConfigStore.ReadAllText();
		ThemeConfigDocument parsedDocument = _themeConfigParser.Parse(rawText);
		ThemeNormalizationResult normalizationResult = _themeConfigNormalizer.Normalize(parsedDocument);
		string normalizedText = ThemeConfigStore.NormalizeLineEndings(normalizationResult.NormalizedText);
		string currentText = ThemeConfigStore.NormalizeLineEndings(rawText);
		bool requiresRewrite = !string.Equals(currentText.Trim(), normalizedText.Trim(), StringComparison.Ordinal);

		if (requiresRewrite)
		{
			_themeConfigStore.WriteAllText(normalizedText);
		}

		ShellThemeDefinition theme = _themeCatalog.GetTheme(normalizationResult.Settings.Theme);
		string statusMessage = BuildStatusMessage(theme, normalizationResult.Messages, origin, requiresRewrite);
		ApplyTheme(theme, statusMessage, requiresRewrite);
	}

	private void ApplyTheme(ShellThemeDefinition theme, string statusMessage, bool configNormalized)
	{
		Action applyAction = () =>
		{
			CurrentTheme = theme;
			CurrentStatusMessage = statusMessage;
			if (Application.Current is null)
			{
				return;
			}

			Application.Current.UserAppTheme = theme.IsDark ? AppTheme.Dark : AppTheme.Light;
			ResourceDictionary resources = Application.Current.Resources;
			ApplyColor(resources, ThemeResourceKeys.CanvasColor, theme.Colors.CanvasColor);
			ApplyColor(resources, ThemeResourceKeys.SurfaceColor, theme.Colors.SurfaceColor);
			ApplyColor(resources, ThemeResourceKeys.SurfaceRaisedColor, theme.Colors.SurfaceRaisedColor);
			ApplyColor(resources, ThemeResourceKeys.SurfaceMutedColor, theme.Colors.SurfaceMutedColor);
			ApplyColor(resources, ThemeResourceKeys.BorderColor, theme.Colors.BorderColor);
			ApplyColor(resources, ThemeResourceKeys.DividerColor, theme.Colors.DividerColor);
			ApplyColor(resources, ThemeResourceKeys.TextPrimaryColor, theme.Colors.TextPrimaryColor);
			ApplyColor(resources, ThemeResourceKeys.TextSecondaryColor, theme.Colors.TextSecondaryColor);
			ApplyColor(resources, ThemeResourceKeys.TextMutedColor, theme.Colors.TextMutedColor);
			ApplyColor(resources, ThemeResourceKeys.AccentColor, theme.Colors.AccentColor);
			ApplyColor(resources, ThemeResourceKeys.AccentSoftColor, theme.Colors.AccentSoftColor);
			ApplyColor(resources, ThemeResourceKeys.AccentStrongColor, theme.Colors.AccentStrongColor);
			ApplyColor(resources, ThemeResourceKeys.EditorBackgroundColor, theme.Colors.EditorBackgroundColor);
			ApplyColor(resources, ThemeResourceKeys.EditorGutterColor, theme.Colors.EditorGutterColor);
			ApplyColor(resources, ThemeResourceKeys.SplitterColor, theme.Colors.SplitterColor);
			ApplyColor(resources, ThemeResourceKeys.StatusBackgroundColor, theme.Colors.StatusBackgroundColor);
			ApplyColor(resources, ThemeResourceKeys.StatusTextColor, theme.Colors.StatusTextColor);
			ApplyColor(resources, ThemeResourceKeys.StatusBorderColor, theme.Colors.StatusBorderColor);

			ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(theme, statusMessage, configNormalized));
		};

		if (MainThread.IsMainThread)
		{
			applyAction();
			return;
		}

		MainThread.BeginInvokeOnMainThread(applyAction);
	}

	private static string BuildStatusMessage(
		ShellThemeDefinition theme,
		IReadOnlyList<string> messages,
		string origin,
		bool configNormalized)
	{
		List<string> parts = [$"theme {theme.Name.ToConfigName()}"];

		if (configNormalized)
		{
			parts.Add("config normalized");
		}

		foreach (string message in messages)
		{
			if (!parts.Contains(message))
			{
				parts.Add(message);
			}
		}

		if (parts.Count == 1 && origin == "startup")
		{
			parts.Add("ready");
		}

		return string.Join("  ", parts);
	}

	private static void ApplyColor(ResourceDictionary resources, string key, string hexColor)
	{
		resources[key] = ThemeSupport.ToColor(hexColor);
	}
}
