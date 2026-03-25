using System.Text.RegularExpressions;

namespace ForRest.Maui.Theming;

public sealed class ThemeConfigParser
{
	private const string ThemeSectionName = "appearance.theme";
	private static readonly Regex ThemeLinePattern = new(
		@"^(?<key>[A-Za-z][\w-]*)\s*=\s*(?<value>[^\r\n#]*?)(\s*(#.*)?)$",
		RegexOptions.Compiled);

	public ThemeConfigDocument Parse(string text)
	{
		List<SettingsTomlLine> lines = [];
		List<ThemeConfigEntry> entries = [];
		List<string> messages = [];
		string[] rawLines = text.Replace("\r\n", "\n").Split('\n');
		string? currentSection = null;
		string licenseKey = string.Empty;

		for (int index = 0; index < rawLines.Length; index++)
		{
			string rawLine = rawLines[index];
			string trimmedLine = rawLine.Trim();

			if (string.IsNullOrWhiteSpace(trimmedLine))
			{
				lines.Add(new SettingsTomlLine(index + 1, rawLine, currentSection, IsBlank: true, IsComment: false, IsSectionHeader: false, IsKeyValue: false, Key: null, Value: null));
				continue;
			}

			if (trimmedLine.StartsWith('#'))
			{
				lines.Add(new SettingsTomlLine(index + 1, rawLine, currentSection, IsBlank: false, IsComment: true, IsSectionHeader: false, IsKeyValue: false, Key: null, Value: null));
				continue;
			}

			if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
			{
				currentSection = trimmedLine[1..^1].Trim();
				lines.Add(new SettingsTomlLine(index + 1, rawLine, currentSection, IsBlank: false, IsComment: false, IsSectionHeader: true, IsKeyValue: false, Key: null, Value: null));
				continue;
			}

			if (!TryParseEntry(rawLine, out string? key, out string? value, out string? message))
			{
				lines.Add(new SettingsTomlLine(index + 1, rawLine, currentSection, IsBlank: false, IsComment: false, IsSectionHeader: false, IsKeyValue: false, Key: null, Value: null));
				if (!string.IsNullOrWhiteSpace(message))
				{
					messages.Add(message);
				}

				continue;
			}

			lines.Add(new SettingsTomlLine(index + 1, rawLine, currentSection, IsBlank: false, IsComment: false, IsSectionHeader: false, IsKeyValue: true, Key: key, Value: value));

			if (string.Equals(currentSection, ThemeSectionName, StringComparison.OrdinalIgnoreCase))
			{
				bool isKnown = ThemeSupport.TryParseThemeName(key!, out _);
				bool? selectedValue = null;
				if (!string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out bool parsedValue))
				{
					selectedValue = parsedValue;
				}
				else if (isKnown)
				{
					messages.Add($"ignored invalid value for '{key}'");
				}

				if (!isKnown)
				{
					messages.Add($"ignored setting entry '{key}'");
				}

				entries.Add(new ThemeConfigEntry(index + 1, key!, selectedValue, isKnown));
				continue;
			}

			if (string.IsNullOrWhiteSpace(currentSection) &&
			    string.Equals(key, SettingsTomlTemplate.LicenseKeyName, StringComparison.OrdinalIgnoreCase))
			{
				licenseKey = ParseScalarValue(value);
			}
		}

		return new ThemeConfigDocument(lines, entries, licenseKey, messages);
	}

	private static bool TryParseEntry(string line, out string? key, out string? value, out string? message)
	{
		key = null;
		value = null;
		message = null;

		Match match = ThemeLinePattern.Match(line);
		if (!match.Success)
		{
			message = "ignored invalid settings line";
			return false;
		}

		key = match.Groups["key"].Value.Trim();
		value = match.Groups["value"].Value.Trim();
		return true;
	}

	private static string ParseScalarValue(string? rawValue)
	{
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			return string.Empty;
		}

		string trimmed = rawValue.Trim();
		if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
		{
			return trimmed[1..^1]
				.Replace("\\\"", "\"", StringComparison.Ordinal)
				.Replace("\\\\", "\\", StringComparison.Ordinal);
		}

		return trimmed;
	}
}
