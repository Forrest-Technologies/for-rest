namespace ForRest.Maui.Theming;

public interface IThemeService
{
	event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

	ShellThemeDefinition CurrentTheme { get; }

	string CurrentStatusMessage { get; }

	string ConfigFilePath { get; }

	void Start();

	void PreviewConfigText(string text);
}
