using System.Text.RegularExpressions;

namespace ForRest.Maui.Theming;

public sealed class ThemeConfigParser
{
	private const string ThemeSectionHeader = "[appearance.theme]";
	private static readonly Regex ThemeLinePattern = new(
		@"^(?<key>[A-Za-z][\w-]*)\s*=\s*(?<value>[^\r\n#]*?)(\s*(#.*)?)$",
		RegexOptions.Compiled);

	public ThemeConfigDocument Parse(string text)
	{
		List<ThemeConfigEntry> entries = [];
		List<string> messages = [];
		string[] lines = text.Replace("\r\n", "\n").Split('\n');
		bool inThemeSection = false;

		foreach (string rawLine in lines)
		{
			string line = rawLine.Trim();
			if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
			{
				continue;
			}

			if (line.StartsWith('[') && line.EndsWith(']'))
			{
				inThemeSection = string.Equals(line, ThemeSectionHeader, StringComparison.OrdinalIgnoreCase);
				continue;
			}

			if (!inThemeSection)
			{
				continue;
			}

			if (!TryParseEntry(line, out ThemeConfigEntry? entry, out string? message) || entry is null)
			{
				if (!string.IsNullOrWhiteSpace(message))
				{
					messages.Add(message);
				}

				continue;
			}

			entries.Add(entry);
		}

		return new ThemeConfigDocument(entries, messages);
	}

	private static bool TryParseEntry(string line, out ThemeConfigEntry? entry, out string? message)
	{
		entry = null;
		message = null;
		Match match = ThemeLinePattern.Match(line);
		if (!match.Success)
		{
			message = "ignored invalid settings line";
			return false;
		}

		string rawName = match.Groups["key"].Value.Trim();
		string rawValue = match.Groups["value"].Value.Trim();
		bool isKnown = ThemeSupport.TryParseThemeName(rawName, out _);
		bool? selectedValue = null;

		if (!string.IsNullOrWhiteSpace(rawValue) && bool.TryParse(rawValue, out bool parsedValue))
		{
			selectedValue = parsedValue;
		}
		else if (isKnown)
		{
			message = $"ignored invalid value for '{rawName}'";
		}

		if (!isKnown)
		{
			message = $"ignored setting entry '{rawName}'";
		}

		entry = new ThemeConfigEntry(rawName, selectedValue, isKnown);
		return true;
	}
}
