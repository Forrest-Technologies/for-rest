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

		ForRestSettings settings = new(selectedTheme, document.LicenseKey)
		{
			Ai = document.Ai
		};

		string normalizedText = Render(document, settings);

		return new ThemeNormalizationResult(settings, normalizedText, messages);
	}

	public string Render(ThemeConfigDocument document, ForRestSettings settings)
	{
		if (document.Lines.Count == 0)
		{
			return _settingsTomlTemplate.Build(settings);
		}

		List<string> output = [];
		bool insertedThemeSection = false;
		bool insertedAiSection = false;
		bool insertedLicense = false;
		bool skippingThemeSection = false;
		bool skippingAiSection = false;
		int licenseInsertIndex = GetLicenseInsertionIndex(document.Lines);

		foreach (SettingsTomlLine line in document.Lines)
		{
			if (line.IsSectionHeader)
			{
				if (skippingThemeSection)
				{
					skippingThemeSection = false;
				}

				if (skippingAiSection)
				{
					skippingAiSection = false;
				}

				if (string.Equals(line.SectionName, "appearance.theme", StringComparison.OrdinalIgnoreCase))
				{
					if (!insertedThemeSection)
					{
						AppendThemeSection(output, settings.Theme);
						insertedThemeSection = true;
					}

					skippingThemeSection = true;
					continue;
				}

				if (string.Equals(line.SectionName, "ai", StringComparison.OrdinalIgnoreCase))
				{
					if (!insertedAiSection)
					{
						AppendAiSection(output, settings.Ai);
						insertedAiSection = true;
					}

					skippingAiSection = true;
					continue;
				}
			}

			if (skippingThemeSection || skippingAiSection)
			{
				continue;
			}

			if (line.IsKeyValue &&
			    string.IsNullOrWhiteSpace(line.SectionName) &&
			    string.Equals(line.Key, SettingsTomlTemplate.LicenseKeyName, StringComparison.OrdinalIgnoreCase))
			{
				if (!insertedLicense)
				{
					output.Add(SettingsTomlTemplate.BuildLicenseLine(settings.LicenseKey));
					insertedLicense = true;
				}

				continue;
			}

			output.Add(line.RawText);
		}

		if (!insertedLicense)
		{
			licenseInsertIndex = Math.Clamp(licenseInsertIndex, 0, output.Count);
			output.Insert(licenseInsertIndex, SettingsTomlTemplate.BuildLicenseLine(settings.LicenseKey));
			if (licenseInsertIndex + 1 < output.Count &&
			    !string.IsNullOrWhiteSpace(output[licenseInsertIndex + 1]))
			{
				output.Insert(licenseInsertIndex + 1, string.Empty);
			}
		}

		if (!insertedThemeSection)
		{
			if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
			{
				output.Add(string.Empty);
			}

			AppendThemeSection(output, settings.Theme);
		}

		if (!insertedAiSection)
		{
			if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
			{
				output.Add(string.Empty);
			}

			AppendAiSection(output, settings.Ai);
		}

		return string.Join(Environment.NewLine, TrimTrailingBlankLines(output));
	}

	private static int GetLicenseInsertionIndex(IReadOnlyList<SettingsTomlLine> lines)
	{
		int index = 0;
		foreach (SettingsTomlLine line in lines)
		{
			if (line.IsBlank || line.IsComment)
			{
				index++;
				continue;
			}

			break;
		}

		return index;
	}

	private static void AppendThemeSection(List<string> output, ShellThemeName selectedTheme)
	{
		output.Add("[appearance.theme]");
		foreach (ShellThemeName theme in ThemeSupport.OrderedThemes)
		{
			output.Add($"{theme.ToConfigName()} = {(theme == selectedTheme ? "true" : "false")}");
		}
	}

	private static void AppendAiSection(List<string> output, ForRestAiSettings ai)
	{
		output.Add(ai.Enabled
			? "# AI settings are enabled."
			: "# AI settings are disabled by default. Set ai.enabled = true to reveal provider, endpoint, model, and api key fields.");
		output.Add("[ai]");
		output.Add($"enabled = {(ai.Enabled ? "true" : "false")}");
		if (!ai.Enabled && !ai.HasConfiguredValues)
		{
			return;
		}

		output.Add($"provider = \"{SettingsTomlTemplate.EscapeTomlString(ai.Provider)}\"");
		output.Add($"api = \"{SettingsTomlTemplate.EscapeTomlString(ai.Api)}\"");
		output.Add($"endpoint = \"{SettingsTomlTemplate.EscapeTomlString(ai.Endpoint)}\"");
		output.Add($"model = \"{SettingsTomlTemplate.EscapeTomlString(ai.Model)}\"");
		output.Add($"deployment_name = \"{SettingsTomlTemplate.EscapeTomlString(ai.DeploymentName)}\"");
		output.Add($"api_key = \"{SettingsTomlTemplate.EscapeTomlString(ai.ApiKey)}\"");
		output.Add($"system_prompt = \"{SettingsTomlTemplate.EscapeTomlString(ai.SystemPrompt)}\"");
	}

	private static IReadOnlyList<string> TrimTrailingBlankLines(List<string> output)
	{
		int lastContentIndex = output.Count - 1;
		while (lastContentIndex >= 0 && string.IsNullOrWhiteSpace(output[lastContentIndex]))
		{
			lastContentIndex--;
		}

		return lastContentIndex < 0 ? [string.Empty] : output.Take(lastContentIndex + 1).ToArray();
	}
}
