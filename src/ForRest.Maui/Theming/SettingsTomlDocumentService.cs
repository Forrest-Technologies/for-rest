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
		ThemeConfigDocument parsedDocument = _themeConfigParser.Parse(rawText);
		ThemeNormalizationResult normalized = _themeConfigNormalizer.Normalize(parsedDocument);
		if (!string.Equals(
			    ThemeConfigStore.NormalizeLineEndings(rawText).Trim(),
			    ThemeConfigStore.NormalizeLineEndings(normalized.NormalizedText).Trim(),
			    StringComparison.Ordinal))
		{
			_themeConfigStore.WriteAllText(normalized.NormalizedText);
		}

		return BuildEditorProjection(parsedDocument, normalized.Settings);
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

		// Resolve theme conflicts relative to the theme currently in effect. When the user flips a
		// second theme flag to true in the editor (e.g. black = true while light is still true), the
		// normalizer must keep the newly enabled one; without the current theme it would fall back to
		// the first flag in the document (light) and silently revert the edit.
		ThemeNormalizationResult editorNormalized = _themeConfigNormalizer.Normalize(
			_themeConfigParser.Parse(text),
			currentNormalized.Settings.Theme);

		ForRestAiSettings editedAi = editorNormalized.Settings.Ai;
		ForRestAiSettings resolvedAi = editedAi with
		{
			ApiKey = string.Equals(editedAi.ApiKey, SettingsTomlTemplate.MaskedSecretValue, StringComparison.Ordinal)
				? currentNormalized.Settings.Ai.ApiKey
				: editedAi.ApiKey
		};

		ForRestSettings nextSettings = editorNormalized.Settings with
		{
			Ai = resolvedAi
		};

		string nextRawText = _themeConfigNormalizer.Render(currentDocument, nextSettings);
		_themeConfigStore.WriteAllText(nextRawText);
	}

	private string BuildEditorProjection(ThemeConfigDocument document, ForRestSettings settings)
	{
		ForRestSettings projectedSettings = settings with
		{
			Ai = settings.Ai with
			{
				ApiKey = string.IsNullOrWhiteSpace(settings.Ai.ApiKey) ? string.Empty : SettingsTomlTemplate.MaskedSecretValue
			}
		};

		return _themeConfigNormalizer.Render(document, projectedSettings);
	}
}
