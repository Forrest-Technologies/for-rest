using System.Text.RegularExpressions;
using ForRest.Services.Licensing;

namespace ForRest.Maui.Theming;

public sealed class SettingsTomlTemplate
{
	public const string LicenseKeyName = "license";
	public const string MaskedLicenseValue = "********";
	public const string GeneratedLicenseInfoSectionHeader = "[license.info]";
	private const string ThemeSectionHeader = "[appearance.theme]";
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
				.. ThemeSupport.OrderedThemes.Select(theme => $"{theme.ToConfigName()} = {(theme == settings.Theme ? "true" : "false")}")
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

	public IReadOnlyList<EditorEditableRange> GetEditableRanges(string text)
	{
		List<EditorEditableRange> ranges = [];
		string[] lines = text.Replace("\r\n", "\n").Split('\n');
		bool inThemeSection = false;

		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			string trimmedLine = line.Trim();

			if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
			{
				inThemeSection = string.Equals(trimmedLine, ThemeSectionHeader, StringComparison.OrdinalIgnoreCase);
				continue;
			}

			if (!inThemeSection)
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
			if (!ThemeSupport.TryParseThemeName(key, out _))
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
		HashSet<ShellThemeName> seenThemes = [];

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
				continue;
			}

			if (!inThemeSection)
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
			if (!ThemeSupport.TryParseThemeName(key, out ShellThemeName theme))
			{
				return false;
			}

			string value = match.Groups["value"].Value.Trim();
			if (!bool.TryParse(value, out _))
			{
				return false;
			}

			seenThemes.Add(theme);
		}

		return ThemeSupport.OrderedThemes.All(seenThemes.Contains);
	}
}
