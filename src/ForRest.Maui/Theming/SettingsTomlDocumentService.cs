namespace ForRest.Maui.Theming;

public sealed class SettingsTomlDocumentService
{
	private readonly ThemeConfigStore _themeConfigStore;
	private readonly ThemeConfigParser _themeConfigParser;
	private readonly ThemeConfigNormalizer _themeConfigNormalizer;
	private readonly SettingsTomlTemplate _settingsTomlTemplate;

	public SettingsTomlDocumentService(
		ThemeConfigStore themeConfigStore,
		ThemeConfigParser themeConfigParser,
		ThemeConfigNormalizer themeConfigNormalizer,
		SettingsTomlTemplate settingsTomlTemplate)
	{
		_themeConfigStore = themeConfigStore;
		_themeConfigParser = themeConfigParser;
		_themeConfigNormalizer = themeConfigNormalizer;
		_settingsTomlTemplate = settingsTomlTemplate;
	}

	public string ConfigFilePath => _themeConfigStore.ConfigFilePath;

	public string LoadOrCreate(ForRestSettings settings)
	{
		string renderedTemplate = _settingsTomlTemplate.Build(settings);
		_themeConfigStore.EnsureConfigFile(renderedTemplate);
		string rawText = _themeConfigStore.ReadAllText();
		ThemeNormalizationResult normalized = _themeConfigNormalizer.Normalize(_themeConfigParser.Parse(rawText));
		if (!string.Equals(
			    ThemeConfigStore.NormalizeLineEndings(rawText).Trim(),
			    ThemeConfigStore.NormalizeLineEndings(normalized.NormalizedText).Trim(),
			    StringComparison.Ordinal))
		{
			_themeConfigStore.WriteAllText(normalized.NormalizedText);
		}

		return BuildEditorProjection(normalized.NormalizedText, normalized.Settings.LicenseKey);
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
		string currentRawText = _themeConfigStore.ReadAllText();
		ThemeConfigDocument currentDocument = _themeConfigParser.Parse(currentRawText);
		ThemeNormalizationResult currentNormalized = _themeConfigNormalizer.Normalize(currentDocument);
		ThemeNormalizationResult editorNormalized = _themeConfigNormalizer.Normalize(_themeConfigParser.Parse(text));

		string editedLicense = editorNormalized.Settings.LicenseKey;
		string resolvedLicense = string.Equals(editedLicense, SettingsTomlTemplate.MaskedLicenseValue, StringComparison.Ordinal)
			? currentNormalized.Settings.LicenseKey
			: editedLicense;
		ForRestSettings nextSettings = editorNormalized.Settings with
		{
			LicenseKey = resolvedLicense
		};

		string nextRawText = _themeConfigNormalizer.Render(currentDocument, nextSettings);
		_themeConfigStore.WriteAllText(nextRawText);
	}

	private string BuildEditorProjection(string rawText, string licenseKey)
	{
		ThemeConfigDocument parsedDocument = _themeConfigParser.Parse(rawText);
		ForRestSettings normalizedSettings = _themeConfigNormalizer.Normalize(parsedDocument).Settings;
		ForRestSettings projectedSettings = normalizedSettings with
		{
			LicenseKey = string.IsNullOrWhiteSpace(licenseKey) ? string.Empty : SettingsTomlTemplate.MaskedLicenseValue
		};
		return _themeConfigNormalizer.Render(parsedDocument, projectedSettings);
	}
}
