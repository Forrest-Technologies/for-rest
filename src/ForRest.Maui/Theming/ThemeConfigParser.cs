using System.Globalization;
using System.Text.RegularExpressions;

namespace ForRest.Maui.Theming;

public sealed class ThemeConfigParser
{
	private const string ThemeSectionName = "appearance.theme";
	private const string StyleSectionName = "appearance.style";
	private const string AiSectionName = "ai";
	private const string McpSectionName = "mcp";
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
		ForRestStyleSettings style = new();
		ForRestAiSettings ai = new();
		ForRestMcpSettings mcp = new();

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

			if (string.Equals(currentSection, StyleSectionName, StringComparison.OrdinalIgnoreCase))
			{
				if (!TryParseStyleEntry(key!, value, style, out ForRestStyleSettings parsedStyle, out string? styleMessage))
				{
					messages.Add(styleMessage ?? $"ignored setting entry '{key}'");
				}
				else
				{
					style = parsedStyle;
				}

				continue;
			}

			if (string.Equals(currentSection, AiSectionName, StringComparison.OrdinalIgnoreCase))
			{
				if (!TryParseAiEntry(key!, value, ai, out ForRestAiSettings parsedAi, out string? aiMessage))
				{
					messages.Add(aiMessage ?? $"ignored setting entry '{key}'");
				}
				else
				{
					ai = parsedAi;
				}
			}

			if (string.Equals(currentSection, McpSectionName, StringComparison.OrdinalIgnoreCase))
			{
				if (!TryParseMcpEntry(key!, value, mcp, out ForRestMcpSettings parsedMcp, out string? mcpMessage))
				{
					messages.Add(mcpMessage ?? $"ignored setting entry '{key}'");
				}
				else
				{
					mcp = parsedMcp;
				}
			}

			if (string.IsNullOrWhiteSpace(currentSection) &&
			    string.Equals(key, SettingsTomlTemplate.LicenseKeyName, StringComparison.OrdinalIgnoreCase))
			{
				licenseKey = ParseScalarValue(value);
			}
		}

		return new ThemeConfigDocument(lines, entries, licenseKey, style, ai, mcp, messages);
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

	private static bool TryParseAiEntry(string key, string? rawValue, ForRestAiSettings current, out ForRestAiSettings parsed, out string? message)
	{
		parsed = current;
		message = null;
		string value = ParseScalarValue(rawValue);

		switch (key.Trim().ToLowerInvariant())
		{
			case "enabled":
				if (!bool.TryParse(value, out bool enabled))
				{
					return SetMessage("ignored invalid value for 'enabled'", out message);
				}

				parsed = current with { Enabled = enabled };
				return true;
			case "provider":
				parsed = current with { Provider = value };
				return true;
			case "api":
				parsed = current with { Api = value };
				return true;
			case "endpoint":
				parsed = current with { Endpoint = value };
				return true;
			case "model":
				parsed = current with { Model = value };
				return true;
			case "deployment_name":
				parsed = current with { DeploymentName = value };
				return true;
			case "api_key":
				parsed = current with { ApiKey = value };
				return true;
			case "system_prompt":
				parsed = current with { SystemPrompt = value };
				return true;
			case "stream_responses":
				if (!bool.TryParse(value, out bool streamResponses))
				{
					return SetMessage("ignored invalid value for 'stream_responses'", out message);
				}

				parsed = current with { StreamResponses = streamResponses };
				return true;
			case "custom_headers":
				parsed = current with { CustomHeaders = value };
				return true;
			default:
				return SetMessage($"ignored setting entry '{key}'", out message);
		}
	}

	private static bool TryParseMcpEntry(string key, string? rawValue, ForRestMcpSettings current, out ForRestMcpSettings parsed, out string? message)
	{
		parsed = current;
		message = null;
		string value = ParseScalarValue(rawValue);

		switch (key.Trim().ToLowerInvariant())
		{
			case "enabled":
				if (!bool.TryParse(value, out bool enabled))
				{
					return SetMessage("ignored invalid value for 'enabled'", out message);
				}

				parsed = current with { Enabled = enabled };
				return true;
			case "bind_address":
				parsed = current with { BindAddress = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value };
				return true;
			case "port":
				if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
				{
					return SetMessage("ignored invalid value for 'port'", out message);
				}

				parsed = current with { Port = port };
				return true;
			case "auth_token":
				parsed = current with { AuthToken = value };
				return true;
			case "max_concurrent_sessions":
				if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sessions) || sessions < 1)
				{
					return SetMessage("ignored invalid value for 'max_concurrent_sessions'", out message);
				}

				parsed = current with { MaxConcurrentSessions = sessions };
				return true;
			default:
				return SetMessage($"ignored setting entry '{key}'", out message);
		}
	}

	private static bool TryParseStyleEntry(string key, string? rawValue, ForRestStyleSettings current, out ForRestStyleSettings parsed, out string? message)
	{
		parsed = current;
		message = null;
		string value = ParseScalarValue(rawValue);

		if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsedValue) ||
		    !double.IsFinite(parsedValue))
		{
			return SetMessage($"ignored invalid value for '{key}'", out message);
		}

		switch (key.Trim().ToLowerInvariant())
		{
			case "editor_font_size":
				parsed = current with { EditorFontSize = parsedValue };
				return true;
			case "result_pane_tab_font_size":
				parsed = current with { ResultPaneTabFontSize = parsedValue };
				return true;
			default:
				return SetMessage($"ignored setting entry '{key}'", out message);
		}
	}

	private static bool SetMessage(string value, out string? message)
	{
		message = value;
		return false;
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
