namespace ForRest.Maui.Theming;

public sealed class ThemeConfigNormalizer
{
	private const ShellThemeName FallbackTheme = ShellThemeName.Light;
	private readonly SettingsTomlTemplate _settingsTomlTemplate;

	public ThemeConfigNormalizer(SettingsTomlTemplate settingsTomlTemplate)
	{
		_settingsTomlTemplate = settingsTomlTemplate;
	}

	public ThemeNormalizationResult Normalize(ThemeConfigDocument document)
	{
		List<string> messages = [.. document.Messages];
		List<ThemeConfigEntry> knownEntries = document.Entries.Where(entry => entry.IsKnown).ToList();
		List<ThemeConfigEntry> selectedEntries = knownEntries.Where(entry => entry.SelectedValue == true).ToList();

		ShellThemeName selectedTheme;
		if (selectedEntries.Count > 1)
		{
			ThemeConfigEntry winningEntry = selectedEntries[0];
			selectedTheme = Enum.Parse<ShellThemeName>(winningEntry.RawName, ignoreCase: true);
			messages.Add("settings normalized");
		}
		else if (selectedEntries.Count == 1)
		{
			selectedTheme = Enum.Parse<ShellThemeName>(selectedEntries[0].RawName, ignoreCase: true);
		}
		else
		{
			selectedTheme = FallbackTheme;
			messages.Add("settings normalized");
		}

		ForRestSettings settings = new(selectedTheme);
		string normalizedText = _settingsTomlTemplate.Build(settings);

		return new ThemeNormalizationResult(settings, normalizedText, messages);
	}
}
