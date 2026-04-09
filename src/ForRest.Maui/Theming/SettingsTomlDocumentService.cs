using ForRest.Licensing;

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
		return LoadOrCreate(
			settings,
			new ActivationSnapshot(
				LicenseAccessStatus.Pending,
				"Activation pending",
				"License state has not been evaluated yet.",
				true));
	}

	public string LoadOrCreate(ForRestSettings settings, ActivationSnapshot activation)
	{
		string renderedTemplate = _settingsTomlTemplate.Build(settings);
		_themeConfigStore.EnsureConfigFile(renderedTemplate);
		string rawText = _themeConfigStore.ReadAllText();
		ThemeConfigDocument parsedDocument = _themeConfigParser.Parse(rawText);
		ThemeNormalizationResult normalized = _themeConfigNormalizer.Normalize(parsedDocument);
		if (!string.Equals(
			    ThemeConfigStore.NormalizeLineEndings(rawText).Trim(),
			    ThemeConfigStore.NormalizeLineEndings(normalized.NormalizedText).Trim(),
			    StringComparison.Ordinal))
		{
			_themeConfigStore.WriteAllText(normalized.NormalizedText);
		}

		return BuildEditorProjection(parsedDocument, normalized.Settings, activation);
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
		string sanitizedText = _settingsTomlTemplate.RemoveGeneratedLicenseInfoBlock(text);
		string currentRawText = _themeConfigStore.ReadAllText();
		ThemeConfigDocument currentDocument = _themeConfigParser.Parse(currentRawText);
		ThemeNormalizationResult currentNormalized = _themeConfigNormalizer.Normalize(currentDocument);
		ThemeNormalizationResult editorNormalized = _themeConfigNormalizer.Normalize(_themeConfigParser.Parse(sanitizedText));

		string editedLicense = editorNormalized.Settings.LicenseKey;
		string resolvedLicense = string.Equals(editedLicense, SettingsTomlTemplate.MaskedLicenseValue, StringComparison.Ordinal)
			? currentNormalized.Settings.LicenseKey
			: editedLicense;

		ForRestAiSettings editedAi = editorNormalized.Settings.Ai;
		ForRestAiSettings resolvedAi = editedAi with
		{
			ApiKey = string.Equals(editedAi.ApiKey, SettingsTomlTemplate.MaskedLicenseValue, StringComparison.Ordinal)
				? currentNormalized.Settings.Ai.ApiKey
				: editedAi.ApiKey
		};

		ForRestSettings nextSettings = editorNormalized.Settings with
		{
			LicenseKey = resolvedLicense,
			Ai = resolvedAi
		};

		string nextRawText = _themeConfigNormalizer.Render(currentDocument, nextSettings);
		_themeConfigStore.WriteAllText(nextRawText);
	}

	private string BuildEditorProjection(ThemeConfigDocument document, ForRestSettings settings, ActivationSnapshot activation)
	{
		ForRestSettings projectedSettings = settings with
		{
			LicenseKey = string.IsNullOrWhiteSpace(settings.LicenseKey) ? string.Empty : SettingsTomlTemplate.MaskedLicenseValue,
			Ai = settings.Ai with
			{
				ApiKey = string.IsNullOrWhiteSpace(settings.Ai.ApiKey) ? string.Empty : SettingsTomlTemplate.MaskedLicenseValue
			}
		};

		string editorText = _themeConfigNormalizer.Render(document, projectedSettings);
		return string.Join(
			Environment.NewLine,
			[
				editorText,
				string.Empty,
				_settingsTomlTemplate.BuildLicenseInfoBlock(activation)
			]);
	}
}
