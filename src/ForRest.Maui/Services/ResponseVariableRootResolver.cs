using System.Text;
using System.Text.RegularExpressions;

namespace ForRest.Maui.Services;

public static class ResponseVariableRootResolver
{
	private static readonly Regex SendAssignmentPattern = new(
		@"^\s*(?:(?:let|var|dynamic)\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:await\s+)?request\.send\s*\(\s*\)\s*;?\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly string[] TopLevelDirectivePrefixes =
	[
		"name",
		"method",
		"url",
		"timeout",
		"max_send_iterations",
		"redirects",
		"ssl",
		"history",
		"content_type",
		"auth",
		"header",
		"query",
		"form",
		"multipart",
		"extract",
		"expect",
		"repeat",
		"retry"
	];

	private static readonly HashSet<string> NonFlowSectionNames =
	[
		"meta",
		"vars",
		"request",
		"auth",
		"query",
		"headers",
		"form",
		"multipart",
		"extract",
		"tests",
		"repeat",
		"retry"
	];

	public static string Resolve(string? sourceText, int lineNumber, string defaultRoot = "response")
	{
		if (!TryResolve(sourceText, lineNumber, out string root))
		{
			return string.IsNullOrWhiteSpace(defaultRoot) ? "response" : defaultRoot.Trim().TrimEnd('.');
		}

		return root;
	}

	public static bool TryResolve(string? sourceText, int lineNumber, out string root)
	{
		root = string.Empty;
		if (string.IsNullOrWhiteSpace(sourceText))
		{
			return false;
		}

		string[] lines = Normalize(sourceText).Split('\n');
		int targetLineNumber = lineNumber > 0 ? Math.Min(lineNumber, lines.Length) : lines.Length;
		List<ScopedSendAssignment> assignments = [];
		int flowDepth = 0;
		int depthAtCursor = 0;
		bool hasExplicitCursorDepth = false;

		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			string trimmed = line.Trim();

			if (string.IsNullOrWhiteSpace(trimmed) || IsComment(trimmed))
			{
				if (index + 1 == targetLineNumber)
				{
					depthAtCursor = flowDepth;
					hasExplicitCursorDepth = true;
				}

				continue;
			}

			if (TryConsumeBody(lines, ref index))
			{
				if (targetLineNumber <= index + 1)
				{
					break;
				}

				continue;
			}

			if (TryConsumeNonFlowSection(lines, ref index))
			{
				if (targetLineNumber <= index + 1)
				{
					break;
				}

				continue;
			}

			bool isFlowLine = flowDepth > 0 || IsFlowLine(trimmed);
			if (!isFlowLine)
			{
				if (index + 1 == targetLineNumber)
				{
					depthAtCursor = flowDepth;
					hasExplicitCursorDepth = true;
				}

				continue;
			}

			int depthBeforeLine = flowDepth;
			if (index + 1 == targetLineNumber)
			{
				depthAtCursor = depthBeforeLine;
				hasExplicitCursorDepth = true;
			}

			if (TryMatchSendAssignment(trimmed, out string variableName))
			{
				assignments.Add(new(variableName, index + 1, depthBeforeLine));
			}

			flowDepth = Math.Max(0, flowDepth + CountBraceDelta(line));
		}

		if (assignments.Count == 0)
		{
			return false;
		}

		ScopedSendAssignment? preferred = hasExplicitCursorDepth && lineNumber > 0
			? assignments
				.Where(assignment => assignment.LineNumber <= targetLineNumber && assignment.ScopeDepth <= depthAtCursor)
				.OrderByDescending(static assignment => assignment.LineNumber)
				.ThenByDescending(static assignment => assignment.ScopeDepth)
				.FirstOrDefault()
			: assignments.LastOrDefault();
		if (preferred is null || string.IsNullOrWhiteSpace(preferred.VariableName))
		{
			return false;
		}

		root = preferred.VariableName;
		return true;
	}

	private static bool TryMatchSendAssignment(string trimmedLine, out string variableName)
	{
		variableName = string.Empty;
		Match match = SendAssignmentPattern.Match(trimmedLine);
		if (!match.Success)
		{
			return false;
		}

		variableName = match.Groups["name"].Value;
		return !string.IsNullOrWhiteSpace(variableName);
	}

	private static bool TryConsumeBody(IReadOnlyList<string> lines, ref int index)
	{
		string trimmed = lines[index].Trim();
		if (!trimmed.StartsWith("body ", StringComparison.OrdinalIgnoreCase) || !trimmed.Contains("\"\"\"", StringComparison.Ordinal))
		{
			return false;
		}

		int delimiterCount = CountTripleQuoteDelimiters(trimmed);
		if (delimiterCount >= 2)
		{
			return true;
		}

		while (++index < lines.Count)
		{
			if (lines[index].Contains("\"\"\"", StringComparison.Ordinal))
			{
				break;
			}
		}

		return true;
	}

	private static bool TryConsumeNonFlowSection(IReadOnlyList<string> lines, ref int index)
	{
		string trimmed = lines[index].Trim();
		if (!trimmed.EndsWith('{'))
		{
			return false;
		}

		string candidate = trimmed[..^1].Trim().ToLowerInvariant();
		if (!NonFlowSectionNames.Contains(candidate))
		{
			return false;
		}

		int depth = CountBraceDelta(lines[index]);
		while (depth > 0 && ++index < lines.Count)
		{
			depth += CountBraceDelta(lines[index]);
		}

		return true;
	}

	private static bool IsFlowLine(string trimmedLine)
	{
		if (string.IsNullOrWhiteSpace(trimmedLine) || IsComment(trimmedLine))
		{
			return false;
		}

		if (trimmedLine.StartsWith("body ", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (trimmedLine.EndsWith('{'))
		{
			string sectionCandidate = trimmedLine[..^1].Trim().ToLowerInvariant();
			if (NonFlowSectionNames.Contains(sectionCandidate))
			{
				return false;
			}
		}

		if (Regex.IsMatch(trimmedLine, @"^(request|runtime)\s+[A-Za-z_][A-Za-z0-9_]*\s*=", RegexOptions.CultureInvariant))
		{
			return false;
		}

		if (Regex.IsMatch(trimmedLine, @"^request\.(method|url|timeout|max_send_iterations|redirects|ssl|history|content_type)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			return false;
		}

		foreach (string prefix in TopLevelDirectivePrefixes)
		{
			if (StartsWithDirective(trimmedLine, prefix))
			{
				return false;
			}
		}

		return true;
	}

	private static bool StartsWithDirective(string text, string directive)
	{
		if (!text.StartsWith(directive, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (text.Length == directive.Length)
		{
			return true;
		}

		char nextCharacter = text[directive.Length];
		return char.IsWhiteSpace(nextCharacter) || nextCharacter == '=';
	}

	private static bool IsComment(string trimmedLine)
	{
		return trimmedLine.StartsWith('#');
	}

	private static int CountTripleQuoteDelimiters(string line)
	{
		if (string.IsNullOrEmpty(line))
		{
			return 0;
		}

		int count = 0;
		int index = 0;
		while ((index = line.IndexOf("\"\"\"", index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += 3;
		}

		return count;
	}

	private static int CountBraceDelta(string line)
	{
		int delta = 0;
		bool inString = false;
		bool escaped = false;
		char quote = '\0';

		foreach (char character in line)
		{
			if (character == '#' && !inString)
			{
				break;
			}

			if (inString)
			{
				if (IsMatchingQuote(quote, character) && !escaped)
				{
					inString = false;
				}
			}
			else if (IsQuote(character))
			{
				inString = true;
				quote = character;
			}
			else if (character == '{')
			{
				delta++;
			}
			else if (character == '}')
			{
				delta--;
			}

			escaped = character == '\\' && !escaped;
			if (character != '\\')
			{
				escaped = false;
			}
		}

		return delta;
	}

	private static bool IsQuote(char character)
	{
		return character is '"' or '\'' or '\u201C' or '\u201D' or '\u201E' or '\u00AB' or '\u00BB';
	}

	private static bool IsMatchingQuote(char openingQuote, char candidate)
	{
		return openingQuote switch
		{
			'"' or '\u201C' or '\u201D' or '\u201E' or '\u00AB' or '\u00BB' => candidate is '"' or '\u201C' or '\u201D' or '\u201E' or '\u00AB' or '\u00BB',
			_ => candidate == openingQuote,
		};
	}

	private static string Normalize(string source)
	{
		return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
	}

	private sealed record ScopedSendAssignment(string VariableName, int LineNumber, int ScopeDepth);
}
