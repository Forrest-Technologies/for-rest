using System.Globalization;
using System.Text.RegularExpressions;

namespace ForRest.Maui.Theming;

public sealed class SettingsTomlTemplate
{
	public const string LicenseKeyName = "license";
	public const string MaskedLicenseValue = "********";
	public const string GeneratedLicenseInfoSectionHeader = "[license.info]";
	private const string ThemeSectionHeader = "[appearance.theme]";
	private const string StyleSectionHeader = "[appearance.style]";
	private const string AiSectionHeader = "[ai]";
	private const string AiEnabledKeyName = "enabled";
	private const string AiProviderKeyName = "provider";
	private const string AiApiKeyName = "api";
	private const string AiEndpointKeyName = "endpoint";
	private const string AiModelKeyName = "model";
	private const string AiDeploymentNameKeyName = "deployment_name";
	private const string AiSecretKeyName = "api_key";
	private const string AiSystemPromptKeyName = "system_prompt";
	private const string AiStreamResponsesKeyName = "stream_responses";
	private const string AiCustomHeadersKeyName = "custom_headers";
	private const string StyleEditorFontSizeKeyName = "editor_font_size";
	private const string StyleResultPaneTabFontSizeKeyName = "result_pane_tab_font_size";
	private static readonly Regex ThemeLinePattern = new(
		@"^(?<indent>\s*)(?<key>[A-Za-z][\w-]*)\s*=\s*(?<value>[^\r\n#]*?)(?<suffix>\s*(#.*)?)$",
		RegexOptions.Compiled);

	public string Build(ForRestSettings settings)
	{
		return string.Join(
			Environment.NewLine,
			[
				"# For-Rest settings are generated from the current settings model.",
				"# Edit only value fields. Structure is enforced and normalized automatically.",
				string.Empty,
				BuildLicenseLine(settings.LicenseKey),
				string.Empty,
				ThemeSectionHeader,
				.. ThemeSupport.OrderedThemes.Select(theme => $"{theme.ToConfigName()} = {(theme == settings.Theme ? "true" : "false")}"),
				string.Empty,
				StyleSectionHeader,
				$"{StyleEditorFontSizeKeyName} = {settings.Style.EditorFontSize.ToString("0.###", CultureInfo.InvariantCulture)}",
				$"{StyleResultPaneTabFontSizeKeyName} = {settings.Style.ResultPaneTabFontSize.ToString("0.###", CultureInfo.InvariantCulture)}",
				string.Empty,
				BuildAiComment(settings.Ai),
				.. BuildAiSection(settings.Ai)
			]);
	}

	public static string BuildLicenseLine(string licenseKey)
	{
		return $"{LicenseKeyName} = \"{EscapeTomlString(licenseKey)}\"";
	}

	public static string EscapeTomlString(string? value)
	{
		return (value ?? string.Empty)
			.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal);
	}

	public string BuildLicenseInfoBlock(ActivationSnapshot activation)
	{
		return string.Join(
			Environment.NewLine,
			[
				"# Read-only activation details. Changes here are ignored.",
				GeneratedLicenseInfoSectionHeader,
				$"state = \"{EscapeTomlString(activation.State.ToString())}\"",
				$"summary = \"{EscapeTomlString(activation.StatusText)}\"",
				$"detail = \"{EscapeTomlString(activation.DetailText)}\"",
				$"registered_to = \"{EscapeTomlString(activation.RegisteredTo)}\"",
				$"registered_email = \"{EscapeTomlString(activation.RegisteredEmail)}\"",
				$"server_validated_utc = \"{EscapeTomlString(FormatDate(activation.ServerValidatedUtc))}\"",
				$"lease_refresh_utc = \"{EscapeTomlString(FormatDate(activation.LeaseRefreshAfterUtc))}\"",
				$"lease_expires_utc = \"{EscapeTomlString(FormatDate(activation.LeaseExpiresUtc))}\"",
				$"license_expires_utc = \"{EscapeTomlString(FormatDate(activation.LicenseExpiresUtc))}\"",
				$"build_grace_ends_utc = \"{EscapeTomlString(FormatDate(activation.BuildGraceExpiresUtc))}\"",
				$"can_execute_requests = {(activation.CanExecuteRequests ? "true" : "false")}"
			]);
	}

	public string RemoveGeneratedLicenseInfoBlock(string text)
	{
		string[] lines = text.Replace("\r\n", "\n").Split('\n');
		List<string> output = [];
		bool skippingSection = false;
		bool skipNextBlankLine = false;

		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			string trimmed = line.Trim();
			if (string.Equals(trimmed, "# Read-only activation details. Changes here are ignored.", StringComparison.Ordinal))
			{
				int lookahead = index + 1;
				while (lookahead < lines.Length && string.IsNullOrWhiteSpace(lines[lookahead]))
				{
					lookahead++;
				}

				if (lookahead < lines.Length &&
				    string.Equals(lines[lookahead].Trim(), GeneratedLicenseInfoSectionHeader, StringComparison.OrdinalIgnoreCase))
				{
					skipNextBlankLine = true;
					continue;
				}
			}

			if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
			{
				if (string.Equals(trimmed, GeneratedLicenseInfoSectionHeader, StringComparison.OrdinalIgnoreCase))
				{
					skippingSection = true;
					continue;
				}

				skippingSection = false;
			}

			if (skippingSection)
			{
				continue;
			}

			if (skipNextBlankLine && string.IsNullOrWhiteSpace(trimmed))
			{
				continue;
			}

			skipNextBlankLine = false;
			output.Add(line);
		}

		while (output.Count > 0 && string.IsNullOrWhiteSpace(output[^1]))
		{
			output.RemoveAt(output.Count - 1);
		}

		return string.Join(Environment.NewLine, output);
	}

	private static string FormatDate(DateTimeOffset? value)
	{
		return value?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? string.Empty;
	}

	private static string FormatDate(DateTimeOffset value)
	{
		return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
	}

	private static string BuildAiComment(ForRestAiSettings ai)
	{
		return ai.Enabled
			? "# AI settings are enabled. provider accepts openai, azure_openai, grok (xai), groq, deepseek, mistral, openrouter, gemini, anthropic, or custom. Most endpoints are optional because For-Rest has sane defaults; Azure OpenAI requires endpoint and deployment_name, custom requires endpoint. Use custom_headers (e.g. \"X-Api-Key: secret; X-Title: For-Rest\") when a provider needs extra headers. stream_responses controls the inline typewriter reveal."
			: "# AI settings are disabled by default. Set ai.enabled = true to reveal provider (openai, azure_openai, grok, groq, deepseek, mistral, openrouter, gemini, anthropic, custom), model, api key, optional endpoint, and optional custom_headers (semicolon-delimited \"Name: value\" pairs). stream_responses controls the inline typewriter reveal.";
	}

	private static IReadOnlyList<string> BuildAiSection(ForRestAiSettings ai)
	{
		List<string> lines =
		[
			AiSectionHeader,
			$"{AiEnabledKeyName} = {(ai.Enabled ? "true" : "false")}",
			$"{AiStreamResponsesKeyName} = {(ai.StreamResponses ? "true" : "false")}"
		];

		if (!ai.Enabled && !ai.HasConfiguredValues)
		{
			return lines;
		}

		lines.Add($"{AiProviderKeyName} = \"{EscapeTomlString(ai.Provider)}\"");
		lines.Add($"{AiApiKeyName} = \"{EscapeTomlString(ai.Api)}\"");
		lines.Add($"{AiEndpointKeyName} = \"{EscapeTomlString(ai.Endpoint)}\"");
		lines.Add($"{AiModelKeyName} = \"{EscapeTomlString(ai.Model)}\"");
		lines.Add($"{AiDeploymentNameKeyName} = \"{EscapeTomlString(ai.DeploymentName)}\"");
		lines.Add($"{AiSecretKeyName} = \"{EscapeTomlString(ai.ApiKey)}\"");
		lines.Add($"{AiSystemPromptKeyName} = \"{EscapeTomlString(ai.SystemPrompt)}\"");
		lines.Add($"{AiCustomHeadersKeyName} = \"{EscapeTomlString(ai.CustomHeaders)}\"");
		return lines;
	}

	public IReadOnlyList<EditorEditableRange> GetEditableRanges(string text)
	{
		List<EditorEditableRange> ranges = [];
		string[] lines = text.Replace("\r\n", "\n").Split('\n');
		bool inThemeSection = false;
		bool inStyleSection = false;
		bool inAiSection = false;

		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			string trimmedLine = line.Trim();

			if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
			{
				inThemeSection = string.Equals(trimmedLine, ThemeSectionHeader, StringComparison.OrdinalIgnoreCase);
				inStyleSection = string.Equals(trimmedLine, StyleSectionHeader, StringComparison.OrdinalIgnoreCase);
				inAiSection = string.Equals(trimmedLine, AiSectionHeader, StringComparison.OrdinalIgnoreCase);
				continue;
			}

			if (!inThemeSection && !inStyleSection && !inAiSection)
			{
				Match topLevelMatch = ThemeLinePattern.Match(line);
				if (topLevelMatch.Success &&
				    string.Equals(topLevelMatch.Groups["key"].Value, LicenseKeyName, StringComparison.OrdinalIgnoreCase))
				{
					Group licenseValueGroup = topLevelMatch.Groups["value"];
					int licenseStartColumn = licenseValueGroup.Index + 1;
					int licenseEndColumn = Math.Max(licenseStartColumn + licenseValueGroup.Length, licenseStartColumn + 1);
					ranges.Add(new EditorEditableRange(index + 1, licenseStartColumn, index + 1, licenseEndColumn));
				}

				continue;
			}

			Match match = ThemeLinePattern.Match(line);
			if (!match.Success)
			{
				continue;
			}

			string key = match.Groups["key"].Value;
			if (inThemeSection)
			{
				if (!ThemeSupport.TryParseThemeName(key, out _))
				{
					continue;
				}
			}
			else if (inStyleSection)
			{
				if (!string.Equals(key, StyleEditorFontSizeKeyName, StringComparison.OrdinalIgnoreCase) &&
				    !string.Equals(key, StyleResultPaneTabFontSizeKeyName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
			}
			else if (!IsKnownAiKey(key))
			{
				continue;
			}

			Group valueGroup = match.Groups["value"];
			int startColumn = valueGroup.Index + 1;
			int endColumn = Math.Max(startColumn + valueGroup.Length, startColumn + 1);
			ranges.Add(new EditorEditableRange(index + 1, startColumn, index + 1, endColumn));
		}

		return ranges;
	}

	public bool CanAutoSave(string text)
	{
		string[] lines = text.Replace("\r\n", "\n").Split('\n');
		bool inThemeSection = false;
		bool inStyleSection = false;
		bool inAiSection = false;
		HashSet<ShellThemeName> seenThemes = [];
		HashSet<string> seenStyleKeys = [];
		HashSet<string> seenAiKeys = [];

		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			string trimmedLine = line.Trim();

			if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
			{
				continue;
			}

			if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
			{
				inThemeSection = string.Equals(trimmedLine, ThemeSectionHeader, StringComparison.OrdinalIgnoreCase);
				inStyleSection = string.Equals(trimmedLine, StyleSectionHeader, StringComparison.OrdinalIgnoreCase);
				inAiSection = string.Equals(trimmedLine, AiSectionHeader, StringComparison.OrdinalIgnoreCase);
				continue;
			}

			if (!inThemeSection && !inStyleSection && !inAiSection)
			{
				Match topLevelMatch = ThemeLinePattern.Match(line);
				if (topLevelMatch.Success &&
				    string.Equals(topLevelMatch.Groups["key"].Value, LicenseKeyName, StringComparison.OrdinalIgnoreCase))
				{
					if (!IsValidStringScalarForAutosave(topLevelMatch.Groups["value"].Value.Trim()))
					{
						return false;
					}

					continue;
				}

				continue;
			}

			Match match = ThemeLinePattern.Match(line);
			if (!match.Success)
			{
				return false;
			}

			string key = match.Groups["key"].Value;
			string value = match.Groups["value"].Value.Trim();

			if (inThemeSection)
			{
				if (!ThemeSupport.TryParseThemeName(key, out ShellThemeName theme))
				{
					return false;
				}

				if (!bool.TryParse(value, out _))
				{
					return false;
				}

				seenThemes.Add(theme);
				continue;
			}

			if (inStyleSection)
			{
				if (!string.Equals(key, StyleEditorFontSizeKeyName, StringComparison.OrdinalIgnoreCase) &&
				    !string.Equals(key, StyleResultPaneTabFontSizeKeyName, StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}

				if (!TryParseStyleNumber(value, out _))
				{
					return false;
				}

				seenStyleKeys.Add(key.ToLowerInvariant());
				continue;
			}

			if (!IsKnownAiKey(key))
			{
				return false;
			}

			if ((string.Equals(key, AiEnabledKeyName, StringComparison.OrdinalIgnoreCase) ||
			     string.Equals(key, AiStreamResponsesKeyName, StringComparison.OrdinalIgnoreCase)) &&
			    !bool.TryParse(value, out _))
			{
				return false;
			}

			if (!string.Equals(key, AiEnabledKeyName, StringComparison.OrdinalIgnoreCase) &&
			    !string.Equals(key, AiStreamResponsesKeyName, StringComparison.OrdinalIgnoreCase) &&
			    !IsValidStringScalarForAutosave(value))
			{
				return false;
			}

			seenAiKeys.Add(key.ToLowerInvariant());
		}

		if (!ThemeSupport.OrderedThemes.All(seenThemes.Contains))
		{
			return false;
		}

		if (!seenStyleKeys.Contains(StyleEditorFontSizeKeyName) ||
		    !seenStyleKeys.Contains(StyleResultPaneTabFontSizeKeyName))
		{
			return false;
		}

		if (seenAiKeys.Count == 0)
		{
			return true;
		}

		if (!seenAiKeys.Contains(AiEnabledKeyName) ||
		    !seenAiKeys.Contains(AiStreamResponsesKeyName))
		{
			return false;
		}

		bool hasAiDetails = seenAiKeys.Any(
			static key => !string.Equals(key, AiEnabledKeyName, StringComparison.OrdinalIgnoreCase) &&
			              !string.Equals(key, AiStreamResponsesKeyName, StringComparison.OrdinalIgnoreCase));
		if (!hasAiDetails)
		{
			return true;
		}

		// custom_headers is intentionally optional so configs written before
		// it existed still autosave cleanly. Everything else is required.
		return seenAiKeys.Contains(AiProviderKeyName) &&
		       seenAiKeys.Contains(AiApiKeyName) &&
		       seenAiKeys.Contains(AiEndpointKeyName) &&
		       seenAiKeys.Contains(AiModelKeyName) &&
		       seenAiKeys.Contains(AiDeploymentNameKeyName) &&
		       seenAiKeys.Contains(AiSecretKeyName) &&
		       seenAiKeys.Contains(AiSystemPromptKeyName);
	}

	private static bool IsKnownAiKey(string key)
	{
		return string.Equals(key, AiEnabledKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiStreamResponsesKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiProviderKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiApiKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiEndpointKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiModelKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiDeploymentNameKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiSecretKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiSystemPromptKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiCustomHeadersKeyName, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsKnownStyleKey(string key)
	{
		return string.Equals(key, StyleEditorFontSizeKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, StyleResultPaneTabFontSizeKeyName, StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryParseStyleNumber(string value, out double result)
	{
		return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result) &&
		       double.IsFinite(result);
	}

	private static bool IsValidStringScalarForAutosave(string value)
	{
		string trimmed = value.Trim();
		if (trimmed.Length == 0)
		{
			return true;
		}

		if (trimmed[0] == '"' || trimmed[^1] == '"')
		{
			return TryParseQuotedString(trimmed);
		}

		return !trimmed.Contains('"') && !trimmed.Contains('\\');
	}

	private static bool TryParseQuotedString(string value)
	{
		if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
		{
			return false;
		}

		for (int index = 1; index < value.Length - 1; index++)
		{
			char character = value[index];
			if (character == '\\')
			{
				if (index + 1 >= value.Length - 1)
				{
					return false;
				}

				char escaped = value[index + 1];
				if (escaped != '\\' && escaped != '"')
				{
					return false;
				}

				index++;
				continue;
			}

			if (character == '"')
			{
				return false;
			}
		}

		return true;
	}

}
