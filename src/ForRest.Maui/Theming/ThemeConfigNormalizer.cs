using System.Globalization;

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
		bool settingsNormalized = false;

		ShellThemeName selectedTheme;
		if (selectedEntries.Count > 1)
		{
			ThemeConfigEntry winningEntry = selectedEntries[0];
			selectedTheme = Enum.Parse<ShellThemeName>(winningEntry.RawName, ignoreCase: true);
			settingsNormalized = true;
		}
		else if (selectedEntries.Count == 1)
		{
			selectedTheme = Enum.Parse<ShellThemeName>(selectedEntries[0].RawName, ignoreCase: true);
		}
		else
		{
			selectedTheme = FallbackTheme;
			settingsNormalized = true;
		}

		ForRestStyleSettings normalizedStyle = document.Style.Normalize();
		if (normalizedStyle != document.Style)
		{
			settingsNormalized = true;
		}

		if (settingsNormalized && !messages.Contains("settings normalized", StringComparer.Ordinal))
		{
			messages.Add("settings normalized");
		}

		ForRestSettings settings = new(selectedTheme, document.LicenseKey)
		{
			Style = normalizedStyle,
			Ai = document.Ai,
			Mcp = document.Mcp
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
		bool insertedStyleSection = false;
		bool insertedAiSection = false;
		bool insertedMcpSection = false;
		bool insertedLicense = false;
		bool skippingThemeSection = false;
		bool skippingStyleSection = false;
		bool skippingAiSection = false;
		bool skippingMcpSection = false;
		int licenseInsertIndex = GetLicenseInsertionIndex(document.Lines);

		foreach (SettingsTomlLine line in document.Lines)
		{
			if (line.IsSectionHeader)
			{
				if (skippingThemeSection)
				{
					skippingThemeSection = false;
				}

				if (skippingStyleSection)
				{
					skippingStyleSection = false;
				}

				if (skippingAiSection)
				{
					skippingAiSection = false;
				}

				if (skippingMcpSection)
				{
					skippingMcpSection = false;
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

				if (string.Equals(line.SectionName, "appearance.style", StringComparison.OrdinalIgnoreCase))
				{
					if (!insertedStyleSection)
					{
						AppendStyleSection(output, settings.Style);
						insertedStyleSection = true;
					}

					skippingStyleSection = true;
					continue;
				}

				if (string.Equals(line.SectionName, "ai", StringComparison.OrdinalIgnoreCase))
				{
					if (!insertedStyleSection)
					{
						AppendStyleSection(output, settings.Style);
						insertedStyleSection = true;
					}

					if (!insertedAiSection)
					{
						AppendAiSection(output, settings.Ai);
						insertedAiSection = true;
					}

					skippingAiSection = true;
					continue;
				}

				if (string.Equals(line.SectionName, "mcp", StringComparison.OrdinalIgnoreCase))
				{
					if (!insertedStyleSection)
					{
						AppendStyleSection(output, settings.Style);
						insertedStyleSection = true;
					}

					if (!insertedAiSection)
					{
						AppendAiSection(output, settings.Ai);
						insertedAiSection = true;
					}

					if (!insertedMcpSection)
					{
						AppendMcpSection(output, settings.Mcp);
						insertedMcpSection = true;
					}

					skippingMcpSection = true;
					continue;
				}
			}

			if (skippingThemeSection || skippingStyleSection || skippingAiSection || skippingMcpSection)
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

		if (!insertedStyleSection)
		{
			if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
			{
				output.Add(string.Empty);
			}

			AppendStyleSection(output, settings.Style);
		}

		if (!insertedAiSection)
		{
			if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
			{
				output.Add(string.Empty);
			}

			AppendAiSection(output, settings.Ai);
		}

		if (!insertedMcpSection)
		{
			if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
			{
				output.Add(string.Empty);
			}

			AppendMcpSection(output, settings.Mcp);
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

	private static void AppendStyleSection(List<string> output, ForRestStyleSettings style)
	{
		output.Add("[appearance.style]");
		output.Add($"editor_font_size = {FormatNumber(style.EditorFontSize)}");
		output.Add($"result_pane_tab_font_size = {FormatNumber(style.ResultPaneTabFontSize)}");
	}

	private static void AppendAiSection(List<string> output, ForRestAiSettings ai)
	{
		output.Add(ai.Enabled
			? "# AI settings are enabled. stream_responses controls the inline typewriter reveal."
			: "# AI settings are disabled by default. Set ai.enabled = true to reveal provider, endpoint, model, and api key fields. stream_responses controls the inline typewriter reveal.");
		output.Add("[ai]");
		output.Add($"enabled = {(ai.Enabled ? "true" : "false")}");
		output.Add($"stream_responses = {(ai.StreamResponses ? "true" : "false")}");
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
		output.Add($"custom_headers = \"{SettingsTomlTemplate.EscapeTomlString(ai.CustomHeaders)}\"");
	}

	private static void AppendMcpSection(List<string> output, ForRestMcpSettings mcp)
	{
		output.Add(mcp.Enabled
			? "# MCP server (desktop-only). Exposes a Model Context Protocol endpoint over TCP so external agents can read the docs, list workspaces, and edit the active request."
			: "# MCP server is disabled by default. Set mcp.enabled = true on a desktop build to expose For-Rest over the Model Context Protocol.");
		output.Add("[mcp]");
		output.Add($"enabled = {(mcp.Enabled ? "true" : "false")}");
		if (!mcp.Enabled && !mcp.HasConfiguredValues)
		{
			return;
		}

		output.Add($"bind_address = \"{SettingsTomlTemplate.EscapeTomlString(mcp.BindAddress)}\"");
		output.Add($"port = {mcp.Port.ToString(CultureInfo.InvariantCulture)}");
		output.Add($"auth_token = \"{SettingsTomlTemplate.EscapeTomlString(mcp.AuthToken)}\"");
		output.Add($"max_concurrent_sessions = {mcp.MaxConcurrentSessions.ToString(CultureInfo.InvariantCulture)}");
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

	private static string FormatNumber(double value)
	{
		return value.ToString("0.###", CultureInfo.InvariantCulture);
	}
}
