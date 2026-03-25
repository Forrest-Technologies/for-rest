using System.Text.RegularExpressions;

namespace ForRest.Maui.Theming;

public sealed class SettingsTomlTemplate
{
	public const string LicenseKeyName = "license";
	public const string MaskedLicenseValue = "********";
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
