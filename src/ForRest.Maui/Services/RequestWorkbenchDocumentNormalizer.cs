using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ForRest.Services.AI;
using ForRest.Scripting;

namespace ForRest.Maui.Services;

public static class RequestWorkbenchDocumentNormalizer
{
	public static string NormalizeRequestDocumentSource(string requestSource, string? preRequestScript, string title)
	{
		string normalizedRequest = NormalizeLineEndings(AiInlineConversationFormatter.RemoveConversationLines(requestSource));
		if (string.IsNullOrWhiteSpace(normalizedRequest))
		{
			return normalizedRequest;
		}

		string sanitizedRequest = NormalizeMixedLegacyRequestSource(normalizedRequest, out bool requestWasSanitized);
		bool shouldNormalize = requestWasSanitized
			|| LooksLikeLegacySectionedDocument(sanitizedRequest)
			|| !string.IsNullOrWhiteSpace(preRequestScript);
		if (!shouldNormalize)
		{
			return sanitizedRequest;
		}

		ForRestScriptParseResult parseResult = new ForRestScriptParser().Parse(sanitizedRequest);
		if (!parseResult.Succeeded || parseResult.Document is null)
		{
			return sanitizedRequest;
		}

		ForRestScriptDocument document = parseResult.Document;
		if (!document.Meta.ContainsKey("name") && !string.IsNullOrWhiteSpace(title))
		{
			document.Meta["name"] = new ForRestScriptStringExpression(title);
		}

		IReadOnlyList<string>? migratedScriptLines = MigrateLegacyScriptToFlowLines(preRequestScript);
		if (migratedScriptLines is not null)
		{
			document = document with
			{
				Flow = MergeFlow(document.Flow, migratedScriptLines)
			};
		}

		return ForRestScriptDocumentRenderer.Render(document);
	}

	public static RequestWorkbenchState NormalizeLoadedWorkbenchState(RequestWorkbenchState state, out bool changed)
	{
		changed = false;
		List<RequestWorkbenchWorkspaceState> normalizedWorkspaces = [];

		foreach (RequestWorkbenchWorkspaceState workspace in state.Workspaces)
		{
			List<RequestWorkbenchDocumentState> normalizedDocuments = [];
			bool workspaceChanged = false;

			foreach (RequestWorkbenchDocumentState document in workspace.Documents)
			{
				string normalizedSource = NormalizeRequestDocumentSource(document.RequestSource, document.PreRequestScript, document.Title);
				if (!string.Equals(NormalizeLineEndings(document.RequestSource), normalizedSource, StringComparison.Ordinal))
				{
					workspaceChanged = true;
				}

				if (!string.IsNullOrWhiteSpace(document.PreRequestScript))
				{
					workspaceChanged = true;
				}

				normalizedDocuments.Add(document with
				{
					RequestSource = normalizedSource,
					PreRequestScript = string.Empty
				});
			}

			if (workspaceChanged)
			{
				changed = true;
			}

			normalizedWorkspaces.Add(workspace with
			{
				Documents = normalizedDocuments
			});
		}

		return changed
			? state with
			{
				Workspaces = normalizedWorkspaces
			}
			: state;
	}

	private static string NormalizeMixedLegacyRequestSource(string requestSource, out bool changed)
	{
		changed = false;
		List<string> normalizedLines = [];
		bool insideBodyBlock = false;

		foreach (string rawLine in NormalizeLineEndings(requestSource).Split('\n'))
		{
			string line = rawLine;
			string trimmed = rawLine.Trim();

			if (insideBodyBlock)
			{
				normalizedLines.Add(line);
				if (CountTripleQuoteTokens(rawLine) % 2 == 1)
				{
					insideBodyBlock = false;
				}
				continue;
			}

			if (StartsBodyBlock(trimmed))
			{
				normalizedLines.Add(line);
				if (CountTripleQuoteTokens(rawLine) % 2 == 1)
				{
					insideBodyBlock = true;
				}
				continue;
			}

			if (TryNormalizeMixedLegacyLine(rawLine, out string? normalizedLine))
			{
				line = normalizedLine!;
				changed = changed || !string.Equals(rawLine, line, StringComparison.Ordinal);
			}

			normalizedLines.Add(line);
		}

		return string.Join(Environment.NewLine, normalizedLines);
	}

	private static IReadOnlyList<string>? MigrateLegacyScriptToFlowLines(string? preRequestScript)
	{
		if (string.IsNullOrWhiteSpace(preRequestScript))
		{
			return null;
		}

		List<string> flowLines = [];
		foreach (string rawLine in NormalizeLineEndings(preRequestScript).Split('\n'))
		{
			string trimmed = rawLine.Trim();
			if (TryRepairLegacyCallClosers(trimmed, out string? repairedLine))
			{
				trimmed = repairedLine!;
			}

			if (string.IsNullOrWhiteSpace(trimmed))
			{
				continue;
			}

			if (trimmed.StartsWith("//", StringComparison.Ordinal))
			{
				flowLines.Add($"# {trimmed[2..].Trim()}");
				continue;
			}

			if (TryMigrateLegacyVariablesSet(trimmed, out string? migratedLine)
			    || TryMigrateLegacyHeaderSet(trimmed, out migratedLine)
			    || TryMigrateLegacyConsole(trimmed, out migratedLine)
			    || TryMigrateLegacySend(trimmed, out migratedLine))
			{
				flowLines.Add(migratedLine!);
				continue;
			}

			flowLines.Add($"# Legacy script requires manual migration: {trimmed}");
		}

		return flowLines.Count == 0 ? null : flowLines;
	}

	private static string MergeFlow(string existingFlow, IReadOnlyList<string> migratedLines)
	{
		string normalizedExistingFlow = NormalizeLineEndings(existingFlow).Trim();
		List<string> merged =
			string.IsNullOrWhiteSpace(normalizedExistingFlow)
				? []
				: [.. normalizedExistingFlow.Split('\n')];
		HashSet<string> seenLines = merged
			.Where(static line => !string.IsNullOrWhiteSpace(line))
			.Select(static line => line.Trim())
			.ToHashSet(StringComparer.Ordinal);

		foreach (string migratedLine in migratedLines)
		{
			string trimmed = migratedLine.Trim();
			if (string.IsNullOrWhiteSpace(trimmed) || !seenLines.Add(trimmed))
			{
				continue;
			}

			if (merged.Count > 0 && !string.IsNullOrWhiteSpace(merged[^1]))
			{
				merged.Add(string.Empty);
			}

			merged.Add(migratedLine);
		}

		return string.Join(Environment.NewLine, merged).Trim();
	}

	private static bool LooksLikeLegacySectionedDocument(string requestSource)
	{
		string normalized = NormalizeLineEndings(requestSource);
		return normalized.Contains("\nmeta {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("meta {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nvars {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("vars {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nrequest {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("request {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nauth {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("auth {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nquery {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("query {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nheaders {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("headers {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nform {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("form {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nmultipart {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("multipart {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nextract {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("extract {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\ntests {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("tests {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nrepeat {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("repeat {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nretry {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("retry {", StringComparison.OrdinalIgnoreCase)
		       || normalized.Contains("\nflow {", StringComparison.OrdinalIgnoreCase)
		       || normalized.StartsWith("flow {", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryNormalizeMixedLegacyLine(string rawLine, out string? normalizedLine)
	{
		normalizedLine = null;
		string indentation = rawLine[..(rawLine.Length - rawLine.TrimStart().Length)];
		string trimmed = rawLine.Trim();
		bool repairedLegacyCall = TryRepairLegacyCallClosers(trimmed, out string? repairedLine);
		if (repairedLegacyCall)
		{
			trimmed = repairedLine!;
		}

		if (string.IsNullOrWhiteSpace(trimmed))
		{
			return false;
		}

		if (TryMigrateLegacyVariablesSet(trimmed, out string? migratedLine)
		    || TryMigrateLegacyHeaderSet(trimmed, out migratedLine)
		    || TryMigrateLegacyConsole(trimmed, out migratedLine)
		    || TryMigrateLegacySend(trimmed, out migratedLine)
		    || TryRepairDanglingExpression(trimmed, out migratedLine))
		{
			normalizedLine = indentation + migratedLine!;
			return true;
		}

		if (repairedLegacyCall)
		{
			normalizedLine = indentation + trimmed;
			return true;
		}

		return false;
	}

	private static bool TryRepairLegacyCallClosers(string line, out string? repairedLine)
	{
		repairedLine = null;
		if (!LooksLikeRepairableLegacyCall(line))
		{
			return false;
		}

		string trimmed = line.TrimEnd();
		bool hadSemicolon = trimmed.EndsWith(';');
		if (hadSemicolon)
		{
			trimmed = trimmed[..^1].TrimEnd();
		}

		int balance = CountParenthesisBalance(trimmed);
		if (balance == 0)
		{
			return false;
		}

		while (balance < 0 && trimmed.EndsWith(')'))
		{
			trimmed = trimmed[..^1].TrimEnd();
			balance++;
		}

		if (balance == 0)
		{
			repairedLine = hadSemicolon ? $"{trimmed};" : trimmed;
			return true;
		}

		repairedLine = trimmed + new string(')', balance);
		if (hadSemicolon)
		{
			repairedLine += ";";
		}

		return true;
	}

	private static bool LooksLikeRepairableLegacyCall(string line)
	{
		return line.StartsWith("console.Log(", StringComparison.Ordinal)
		       || line.StartsWith("console.Warn(", StringComparison.Ordinal)
		       || line.StartsWith("console.Error(", StringComparison.Ordinal)
		       || line.StartsWith("request.SetHeader(", StringComparison.Ordinal)
		       || line.StartsWith("variables.Set(", StringComparison.Ordinal)
		       || line.StartsWith("await request.send(", StringComparison.Ordinal)
		       || line.StartsWith("request.send(", StringComparison.Ordinal)
		       || line.StartsWith("var sent = await request.send(", StringComparison.Ordinal)
		       || line.StartsWith("let sent = await request.send(", StringComparison.Ordinal)
		       || line.StartsWith("var sent = request.send(", StringComparison.Ordinal)
		       || line.StartsWith("let sent = request.send(", StringComparison.Ordinal);
	}

	private static bool TryMigrateLegacyVariablesSet(string line, out string? flowLine)
	{
		flowLine = null;
		const string prefix = "variables.Set(";
		if (!TryExtractLegacyCallArguments(line, prefix, out string? inner))
		{
			return false;
		}

		int commaIndex = inner!.IndexOf(',');
		if (commaIndex <= 0)
		{
			return false;
		}

		string rawName = inner[..commaIndex].Trim();
		string rawValue = inner[(commaIndex + 1)..].Trim();
		if (!rawName.StartsWith('"') || !rawName.EndsWith('"'))
		{
			return false;
		}

		flowLine = $"runtime {rawName[1..^1]} = {rawValue}";
		return true;
	}

	private static bool TryMigrateLegacyHeaderSet(string line, out string? flowLine)
	{
		flowLine = null;
		const string prefix = "request.SetHeader(";
		if (!TryExtractLegacyCallArguments(line, prefix, out string? inner))
		{
			return false;
		}

		int commaIndex = inner!.IndexOf(',');
		if (commaIndex <= 0)
		{
			return false;
		}

		string rawHeader = inner[..commaIndex].Trim();
		string rawValue = inner[(commaIndex + 1)..].Trim();
		flowLine = $"request.headers[{rawHeader}] = {rawValue}";
		return true;
	}

	private static bool TryMigrateLegacyConsole(string line, out string? flowLine)
	{
		flowLine = null;
		foreach ((string Prefix, string Command) mapping in new[]
		         {
			         ("console.Log(", "log"),
			         ("console.Warn(", "warn"),
			         ("console.Error(", "error"),
		         })
		{
			if (!TryExtractLegacyCallArguments(line, mapping.Prefix, out string? argumentText))
			{
				continue;
			}

			flowLine = $"{mapping.Command} {argumentText!.Trim()}";
			return true;
		}

		return false;
	}

	private static bool TryMigrateLegacySend(string line, out string? flowLine)
	{
		flowLine = null;
		Match match = Regex.Match(
			line,
			@"^(?:(?:var|let)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*)?(?:await\s+)?request\.send\s*\(\s*\)(?:\s*\))*\s*;?\s*$",
			RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return false;
		}

		string variableName = match.Groups["name"].Value;
		flowLine = string.IsNullOrWhiteSpace(variableName)
			? "request.send()"
			: $"let {variableName} = request.send()";
		return true;
	}

	private static bool TryExtractLegacyCallArguments(string line, string prefix, out string? inner)
	{
		inner = null;
		if (!line.StartsWith(prefix, StringComparison.Ordinal))
		{
			return false;
		}

		string trimmed = line.TrimEnd();
		if (trimmed.EndsWith(';'))
		{
			trimmed = trimmed[..^1].TrimEnd();
		}

		if (!trimmed.EndsWith(')'))
		{
			return false;
		}

		inner = trimmed[prefix.Length..^1];
		return true;
	}

	private static bool TryRepairDanglingExpression(string line, out string? repairedLine)
	{
		repairedLine = null;
		if (!TryFindAssignmentSeparatorIndex(line, out int separatorIndex))
		{
			return false;
		}

		string rawValue = line[(separatorIndex + 1)..].Trim();
		string repairedValue = TrimDanglingTrailingClosers(rawValue);
		if (string.Equals(repairedValue, rawValue, StringComparison.Ordinal))
		{
			return false;
		}

		repairedLine = $"{line[..separatorIndex].TrimEnd()} = {repairedValue}";
		return true;
	}

	private static bool TryFindAssignmentSeparatorIndex(string line, out int separatorIndex)
	{
		separatorIndex = -1;
		bool inString = false;
		bool escaped = false;
		int parenthesisDepth = 0;
		int bracketDepth = 0;
		int braceDepth = 0;

		for (int index = 0; index < line.Length; index++)
		{
			char character = line[index];
			if (inString)
			{
				if (character == '"' && !escaped)
				{
					inString = false;
				}

				escaped = character == '\\' && !escaped;
				if (character != '\\')
				{
					escaped = false;
				}
				continue;
			}

			switch (character)
			{
				case '"':
					inString = true;
					escaped = false;
					continue;

				case '(':
					parenthesisDepth++;
					continue;

				case ')':
					parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
					continue;

				case '[':
					bracketDepth++;
					continue;

				case ']':
					bracketDepth = Math.Max(0, bracketDepth - 1);
					continue;

				case '{':
					braceDepth++;
					continue;

				case '}':
					braceDepth = Math.Max(0, braceDepth - 1);
					continue;

				case '=' when parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0:
				{
					char previous = FindPreviousNonWhitespaceCharacter(line, index);
					char next = FindNextNonWhitespaceCharacter(line, index);
					if (previous is '>' or '<' or '!' or '=' ||
					    next is '=' or '>')
					{
						continue;
					}

					separatorIndex = index;
					return true;
				}
			}
		}

		return false;
	}

	private static char FindPreviousNonWhitespaceCharacter(string line, int startIndex)
	{
		for (int index = startIndex - 1; index >= 0; index--)
		{
			if (!char.IsWhiteSpace(line[index]))
			{
				return line[index];
			}
		}

		return '\0';
	}

	private static char FindNextNonWhitespaceCharacter(string line, int startIndex)
	{
		for (int index = startIndex + 1; index < line.Length; index++)
		{
			if (!char.IsWhiteSpace(line[index]))
			{
				return line[index];
			}
		}

		return '\0';
	}

	private static string TrimDanglingTrailingClosers(string value)
	{
		string trimmed = value.Trim();
		bool hadSemicolon = trimmed.EndsWith(';');
		if (hadSemicolon)
		{
			trimmed = trimmed[..^1].TrimEnd();
		}

		int balance = CountParenthesisBalance(trimmed);
		while (balance < 0 && trimmed.EndsWith(')'))
		{
			trimmed = trimmed[..^1].TrimEnd();
			balance++;
		}

		return hadSemicolon ? $"{trimmed};" : trimmed;
	}

	private static int CountParenthesisBalance(string value)
	{
		int balance = 0;
		bool inString = false;
		bool escaped = false;

		foreach (char character in value)
		{
			if (inString)
			{
				if (character == '"' && !escaped)
				{
					inString = false;
				}

				escaped = character == '\\' && !escaped;
				if (character != '\\')
				{
					escaped = false;
				}
				continue;
			}

			if (character == '"')
			{
				inString = true;
				escaped = false;
				continue;
			}

			if (character == '(')
			{
				balance++;
			}
			else if (character == ')')
			{
				balance--;
			}
		}

		return balance;
	}

	private static bool StartsBodyBlock(string trimmed)
	{
		return trimmed.StartsWith("body ", StringComparison.OrdinalIgnoreCase)
		       && trimmed.Contains("\"\"\"", StringComparison.Ordinal);
	}

	private static int CountTripleQuoteTokens(string value)
	{
		int count = 0;
		int index = 0;
		while ((index = value.IndexOf("\"\"\"", index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += 3;
		}

		return count;
	}

	private static string NormalizeLineEndings(string text)
	{
		return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
	}
}
