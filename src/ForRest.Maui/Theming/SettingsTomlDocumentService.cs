namespace ForRest.Maui.Theming;

public sealed class SettingsTomlDocumentService
{
	private readonly ThemeConfigStore _themeConfigStore;
	private readonly SettingsTomlTemplate _settingsTomlTemplate;

	public SettingsTomlDocumentService(
		ThemeConfigStore themeConfigStore,
		SettingsTomlTemplate settingsTomlTemplate)
	{
		_themeConfigStore = themeConfigStore;
		_settingsTomlTemplate = settingsTomlTemplate;
	}

	public string ConfigFilePath => _themeConfigStore.ConfigFilePath;

	public string LoadOrCreate(ForRestSettings settings)
	{
		string renderedTemplate = _settingsTomlTemplate.Build(settings);
		_themeConfigStore.EnsureConfigFile(renderedTemplate);
		return renderedTemplate;
	}

	public IReadOnlyList<EditorEditableRange> GetEditableRanges(string text)
	{
		return _settingsTomlTemplate.GetEditableRanges(text);
	}

	public bool CanAutoSave(string text)
	{
		return _settingsTomlTemplate.CanAutoSave(text);
	}

	public void SaveRawText(string text)
	{
		_themeConfigStore.WriteAllText(text);
	}
}
