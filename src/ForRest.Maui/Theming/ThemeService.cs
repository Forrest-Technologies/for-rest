using Microsoft.Maui.ApplicationModel;
using ForRest.Maui.Services;

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
		CurrentSettings = new ForRestSettings(ShellThemeName.Azure);
		CurrentTheme = _themeCatalog.GetTheme(CurrentSettings.Theme);
		CurrentStatusMessage = "Ready";
	}

	public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

	public ShellThemeDefinition CurrentTheme { get; private set; }

	public ForRestSettings CurrentSettings { get; private set; }

	public string CurrentStatusMessage { get; private set; }

	public string ConfigFilePath => _themeConfigStore.ConfigFilePath;

	public void Start()
	{
		if (_started)
		{
			return;
		}

		_started = true;
		string initialConfig = _settingsTomlTemplate.Build(CurrentSettings);

		_themeConfigStore.EnsureConfigFile(initialConfig);
		ProcessConfigCore("startup");
		if (PlatformExperience.SupportsThemeConfigWatcher())
		{
			TryStartWatcher();
		}
	}

	public void PreviewConfigText(string text)
	{
		_processingLock.Wait();
		try
		{
			string rawText = ThemeConfigStore.NormalizeLineEndings(text);
			ThemeConfigDocument parsedDocument = _themeConfigParser.Parse(rawText);
			ThemeNormalizationResult normalizationResult = _themeConfigNormalizer.Normalize(parsedDocument, CurrentSettings.Theme);
			ShellThemeDefinition theme = _themeCatalog.GetTheme(normalizationResult.Settings.Theme);
			string statusMessage = BuildStatusMessage(theme, normalizationResult.Messages, "preview", configNormalized: false);
			ApplyTheme(theme, normalizationResult.Settings, statusMessage, configNormalized: false, isPreview: true);
		}
		finally
		{
			_processingLock.Release();
		}
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

	private void TryStartWatcher()
	{
		try
		{
			StartWatcher();
		}
		catch (Exception exception)
		{
			CurrentStatusMessage = string.IsNullOrWhiteSpace(CurrentStatusMessage)
				? "ready  config watch unavailable"
				: $"{CurrentStatusMessage}  config watch unavailable";
			AppLaunchGuard.RecordException("Theme config watcher startup failed.", exception);
		}
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
		ThemeNormalizationResult normalizationResult = _themeConfigNormalizer.Normalize(parsedDocument, CurrentSettings.Theme);
		string normalizedText = ThemeConfigStore.NormalizeLineEndings(normalizationResult.NormalizedText);
		string currentText = ThemeConfigStore.NormalizeLineEndings(rawText);
		bool requiresRewrite = !string.Equals(currentText.Trim(), normalizedText.Trim(), StringComparison.Ordinal);

		if (requiresRewrite)
		{
			_themeConfigStore.WriteAllText(normalizedText);
		}

		ShellThemeDefinition theme = _themeCatalog.GetTheme(normalizationResult.Settings.Theme);
		string statusMessage = BuildStatusMessage(theme, normalizationResult.Messages, origin, requiresRewrite);
		ApplyTheme(theme, normalizationResult.Settings, statusMessage, requiresRewrite, isPreview: false);
	}

	private void ApplyTheme(ShellThemeDefinition theme, ForRestSettings settings, string statusMessage, bool configNormalized, bool isPreview)
	{
		Action applyAction = () =>
		{
			CurrentTheme = theme;
			CurrentSettings = settings;
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
			ApplyColor(resources, ThemeResourceKeys.AccentTextColor, theme.Colors.AccentTextColor);
			ApplyColor(resources, ThemeResourceKeys.SelectionSoftColor, theme.Colors.SelectionSoftColor);
			ApplyColor(resources, ThemeResourceKeys.SelectionStrongColor, theme.Colors.SelectionStrongColor);
			ApplyColor(resources, ThemeResourceKeys.SelectionBorderColor, theme.Colors.SelectionBorderColor);
			ApplyColor(resources, ThemeResourceKeys.SelectionTextColor, theme.Colors.SelectionTextColor);
			ApplyColor(resources, ThemeResourceKeys.EditorBackgroundColor, theme.Colors.EditorBackgroundColor);
			ApplyColor(resources, ThemeResourceKeys.EditorGutterColor, theme.Colors.EditorGutterColor);
			ApplyColor(resources, ThemeResourceKeys.SplitterColor, theme.Colors.SplitterColor);
			ApplyColor(resources, ThemeResourceKeys.OverlayBackdropColor, theme.Colors.OverlayBackdropColor);
			ApplyColor(resources, ThemeResourceKeys.StatusBackgroundColor, theme.Colors.StatusBackgroundColor);
			ApplyColor(resources, ThemeResourceKeys.StatusTextColor, theme.Colors.StatusTextColor);
			ApplyColor(resources, ThemeResourceKeys.StatusBorderColor, theme.Colors.StatusBorderColor);
			ApplyColor(resources, ThemeResourceKeys.SuccessColor, theme.Colors.SuccessColor);
			ApplyColor(resources, ThemeResourceKeys.WarningColor, theme.Colors.WarningColor);
			ApplyColor(resources, ThemeResourceKeys.DangerColor, theme.Colors.DangerColor);
			ApplyColor(resources, ThemeResourceKeys.MethodGetColor, theme.Colors.MethodGetColor);
			ApplyColor(resources, ThemeResourceKeys.MethodPostColor, theme.Colors.MethodPostColor);
			ApplyColor(resources, ThemeResourceKeys.MethodPutColor, theme.Colors.MethodPutColor);
			ApplyColor(resources, ThemeResourceKeys.MethodPatchColor, theme.Colors.MethodPatchColor);
			ApplyColor(resources, ThemeResourceKeys.MethodDeleteColor, theme.Colors.MethodDeleteColor);
			ApplyColor(resources, ThemeResourceKeys.MethodNeutralColor, theme.Colors.MethodNeutralColor);
			ApplyColor(resources, ThemeResourceKeys.WindowChromeBackgroundColor, theme.Colors.WindowChromeBackgroundColor);
			ApplyColor(resources, ThemeResourceKeys.WindowChromeForegroundColor, theme.Colors.WindowChromeForegroundColor);
			ApplyColor(resources, ThemeResourceKeys.WindowChromeInactiveBackgroundColor, theme.Colors.WindowChromeInactiveBackgroundColor);
			ApplyColor(resources, ThemeResourceKeys.WindowChromeInactiveForegroundColor, theme.Colors.WindowChromeInactiveForegroundColor);

			ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(theme, settings, statusMessage, configNormalized, isPreview));
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
