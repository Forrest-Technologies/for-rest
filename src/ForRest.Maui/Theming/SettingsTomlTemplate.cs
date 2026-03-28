using System.Text.RegularExpressions;
using ForRest.Services.Licensing;

namespace ForRest.Maui.Theming;

public sealed class SettingsTomlTemplate
{
	public const string LicenseKeyName = "license";
	public const string MaskedLicenseValue = "********";
	public const string GeneratedLicenseInfoSectionHeader = "[license.info]";
	private const string ThemeSectionHeader = "[appearance.theme]";
	private const string AiSectionHeader = "[ai]";
	private const string AiEnabledKeyName = "enabled";
	private const string AiProviderKeyName = "provider";
	private const string AiApiKeyName = "api";
	private const string AiEndpointKeyName = "endpoint";
	private const string AiModelKeyName = "model";
	private const string AiDeploymentNameKeyName = "deployment_name";
	private const string AiSecretKeyName = "api_key";
	private const string AiSystemPromptKeyName = "system_prompt";
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

	public string BuildLicenseInfoBlock(LicenseValidationResult validation)
	{
		return string.Join(
			Environment.NewLine,
			[
				"# Read-only activation details. Changes here are ignored.",
				GeneratedLicenseInfoSectionHeader,
				$"state = \"{EscapeTomlString(validation.Status.ToString())}\"",
				$"summary = \"{EscapeTomlString(validation.Summary)}\"",
				$"detail = \"{EscapeTomlString(validation.Detail)}\"",
				$"registered_to = \"{EscapeTomlString(validation.RegisteredTo)}\"",
				$"registered_email = \"{EscapeTomlString(validation.RegisteredEmail)}\"",
				$"license_expires_utc = \"{EscapeTomlString(FormatDate(validation.LicenseExpirationUtc))}\"",
				$"beta_trial_active = {(validation.IsGraceActive ? "true" : "false")}",
				$"build_started_utc = \"{EscapeTomlString(FormatDate(validation.BuildDateUtc))}\"",
				$"beta_trial_ends_utc = \"{EscapeTomlString(FormatDate(validation.GraceExpiresUtc))}\"",
				$"grace_days_remaining = {validation.GraceDaysRemaining}",
				$"can_execute_requests = {(validation.IsExecutionAllowed ? "true" : "false")}"
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
			? "# AI settings are enabled."
			: "# AI settings are disabled by default. Set ai.enabled = true to reveal provider, endpoint, model, and api key fields.";
	}

	private static IReadOnlyList<string> BuildAiSection(ForRestAiSettings ai)
	{
		List<string> lines =
		[
			AiSectionHeader,
			$"{AiEnabledKeyName} = {(ai.Enabled ? "true" : "false")}"
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
		return lines;
	}

	public IReadOnlyList<EditorEditableRange> GetEditableRanges(string text)
	{
		List<EditorEditableRange> ranges = [];
		string[] lines = text.Replace("\r\n", "\n").Split('\n');
		bool inThemeSection = false;
		bool inAiSection = false;

		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			string trimmedLine = line.Trim();

			if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
			{
				inThemeSection = string.Equals(trimmedLine, ThemeSectionHeader, StringComparison.OrdinalIgnoreCase);
				inAiSection = string.Equals(trimmedLine, AiSectionHeader, StringComparison.OrdinalIgnoreCase);
				continue;
			}

			if (!inThemeSection && !inAiSection)
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
		bool inAiSection = false;
		HashSet<ShellThemeName> seenThemes = [];
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
				inAiSection = string.Equals(trimmedLine, AiSectionHeader, StringComparison.OrdinalIgnoreCase);
				continue;
			}

			if (!inThemeSection && !inAiSection)
			{
				Match topLevelMatch = ThemeLinePattern.Match(line);
				if (topLevelMatch.Success &&
				    string.Equals(topLevelMatch.Groups["key"].Value, LicenseKeyName, StringComparison.OrdinalIgnoreCase))
				{
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

			if (!IsKnownAiKey(key))
			{
				return false;
			}

			if (string.Equals(key, AiEnabledKeyName, StringComparison.OrdinalIgnoreCase) &&
			    !bool.TryParse(value, out _))
			{
				return false;
			}

			seenAiKeys.Add(key.ToLowerInvariant());
		}

		if (!ThemeSupport.OrderedThemes.All(seenThemes.Contains))
		{
			return false;
		}

		if (seenAiKeys.Count == 0)
		{
			return true;
		}

		if (!seenAiKeys.Contains(AiEnabledKeyName))
		{
			return false;
		}

		bool hasAiDetails = seenAiKeys.Any(static key => !string.Equals(key, AiEnabledKeyName, StringComparison.OrdinalIgnoreCase));
		if (!hasAiDetails)
		{
			return true;
		}

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
		       string.Equals(key, AiProviderKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiApiKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiEndpointKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiModelKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiDeploymentNameKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiSecretKeyName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(key, AiSystemPromptKeyName, StringComparison.OrdinalIgnoreCase);
	}
}
