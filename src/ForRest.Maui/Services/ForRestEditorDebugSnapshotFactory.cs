using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ForRest.Models;
using ForRest.Scripting;

namespace ForRest.Maui.Services;

public enum ForRestEditorDebugState
{
	Ready,
	Warning,
	Error,
}

public sealed record ForRestEditorDebugSnapshot(
	ForRestEditorDebugState State,
	string StatusText,
	string SummaryText,
	string DetailText,
	string DiagnosticsJson,
	string DebugOutputText);

public sealed record ForRestEditorDiagnosticMarker(
	string Severity,
	string Message,
	int StartLineNumber,
	int StartColumn,
	int EndLineNumber,
	int EndColumn);

public static class ForRestEditorDebugSnapshotFactory
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	public static ForRestEditorDebugSnapshot Create(
		string source,
		ForRestScriptCompilationResult compilation,
		string workspaceName,
		string environmentName,
		string fallbackRequestName,
		string fallbackTarget)
	{
		string normalizedSource = NormalizeLineEndings(source);
		string[] lines = normalizedSource.Length == 0 ? [""] : normalizedSource.Split('\n');
		ForRestEditorDebugState state = ResolveState(compilation.Diagnostics);
		string statusText = state switch
		{
			ForRestEditorDebugState.Error => "Compile error",
			ForRestEditorDebugState.Warning => "Ready with warnings",
			_ => "Ready",
		};

		RequestDefinition? request = compilation.Payload?.Request;
		string requestName = string.IsNullOrWhiteSpace(request?.Name) ? fallbackRequestName : request.Name;
		string method = request?.Method.ToString().ToUpperInvariant() ?? "GET";
		string target = string.IsNullOrWhiteSpace(request?.UrlTemplate) ? fallbackTarget : request.UrlTemplate;
		int runtimeSeedCount = compilation.Payload?.RuntimeSeeds.Count ?? 0;
		int requestVariableCount = request?.Variables.Count ?? 0;
		int testCount = compilation.Document?.Tests.Count ?? 0;
		int extractionCount = request?.Extractions.Count ?? compilation.Document?.Extractions.Count ?? 0;
		int sendBudget = request?.MaxSendIterations ?? 0;
		string summaryText = state == ForRestEditorDebugState.Error
			? BuildFailureSummary(compilation.Diagnostics)
			: $"{method} {requestName}".Trim();
		string detailText = state == ForRestEditorDebugState.Error
			? BuildFailureDetail(compilation.Diagnostics, lines.Length)
			: $"{target}  send<={sendBudget}  vars {requestVariableCount + runtimeSeedCount}  tests {testCount}  extracts {extractionCount}";

		return new ForRestEditorDebugSnapshot(
			State: state,
			StatusText: statusText,
			SummaryText: summaryText,
			DetailText: detailText,
			DiagnosticsJson: BuildDiagnosticsJson(compilation.Diagnostics, lines),
			DebugOutputText: BuildDebugOutput(
				compilation,
				workspaceName,
				environmentName,
				requestName,
				method,
				target,
				statusText,
				requestVariableCount,
				runtimeSeedCount,
				testCount,
				extractionCount,
				sendBudget,
				lines.Length));
	}

	private static ForRestEditorDebugState ResolveState(IReadOnlyList<ForRestScriptDiagnostic> diagnostics)
	{
		if (diagnostics.Any(static diagnostic => diagnostic.Severity == ForRestScriptDiagnosticSeverity.Error))
		{
			return ForRestEditorDebugState.Error;
		}

		return diagnostics.Any(static diagnostic => diagnostic.Severity == ForRestScriptDiagnosticSeverity.Warning)
			? ForRestEditorDebugState.Warning
			: ForRestEditorDebugState.Ready;
	}

	private static string BuildFailureSummary(IReadOnlyList<ForRestScriptDiagnostic> diagnostics)
	{
		ForRestScriptDiagnostic? first = diagnostics.FirstOrDefault();
		return string.IsNullOrWhiteSpace(first?.Message)
			? "Fix highlighted compile issues."
			: first.Message;
	}

	private static string BuildFailureDetail(IReadOnlyList<ForRestScriptDiagnostic> diagnostics, int lineCount)
	{
		if (diagnostics.Count == 0)
		{
			return "The script did not compile.";
		}

		ForRestScriptDiagnostic first = diagnostics[0];
		int line = NormalizeLine(first.Line, lineCount);
		int column = Math.Max(1, first.Column);
		return $"{diagnostics.Count} diagnostic(s)  first at L{line}:C{column}";
	}

    private static string BuildDiagnosticsJson(IReadOnlyList<ForRestScriptDiagnostic> diagnostics, IReadOnlyList<string> lines)
    {
        if (diagnostics.Count == 0)
        {
            return "[]";
        }

        List<ForRestEditorDiagnosticMarker> markers =
        [
            .. diagnostics
                .Where(static diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0)
                .Select(diagnostic => CreateMarker(diagnostic, lines)),
        ];

        return markers.Count == 0
            ? "[]"
            : JsonSerializer.Serialize(markers, JsonOptions);
    }

	private static ForRestEditorDiagnosticMarker CreateMarker(ForRestScriptDiagnostic diagnostic, IReadOnlyList<string> lines)
	{
		int startLineNumber = NormalizeLine(diagnostic.Line, lines.Count);
		string lineText = lines[Math.Clamp(startLineNumber - 1, 0, Math.Max(0, lines.Count - 1))];
		int lineMaxColumn = Math.Max(1, lineText.Length + 1);
		int startColumn = Math.Clamp(Math.Max(1, diagnostic.Column), 1, lineMaxColumn);
		int endColumn = DetermineMarkerEndColumn(lineText, startColumn, lineMaxColumn);

		return new ForRestEditorDiagnosticMarker(
			Severity: diagnostic.Severity == ForRestScriptDiagnosticSeverity.Warning ? "Warning" : "Error",
			Message: string.IsNullOrWhiteSpace(diagnostic.Message) ? "Unknown ForRest diagnostic." : diagnostic.Message,
			StartLineNumber: startLineNumber,
			StartColumn: startColumn,
			EndLineNumber: startLineNumber,
			EndColumn: endColumn);
	}

	private static int DetermineMarkerEndColumn(string lineText, int startColumn, int lineMaxColumn)
	{
		if (string.IsNullOrEmpty(lineText))
		{
			return Math.Max(startColumn, 2);
		}

		int startIndex = Math.Clamp(startColumn - 1, 0, Math.Max(0, lineText.Length - 1));
		if (char.IsWhiteSpace(lineText[startIndex]))
		{
			return Math.Min(lineMaxColumn, startColumn + 1);
		}

		if (lineText[startIndex] is '"' or '\'' or '\u201C' or '\u201D' or '\u201E' or '\u00AB' or '\u00BB')
		{
			bool escaped = false;
			for (int index = startIndex + 1; index < lineText.Length; index++)
			{
				char character = lineText[index];
				if ((character is '"' or '\'' or '\u201C' or '\u201D' or '\u201E' or '\u00AB' or '\u00BB') && !escaped)
				{
					return Math.Min(lineMaxColumn, index + 2);
				}

				if (character == '\\' && !escaped)
				{
					escaped = true;
				}
				else
				{
					escaped = false;
				}
			}

			return lineMaxColumn;
		}

		int endIndex = startIndex + 1;
		while (endIndex < lineText.Length)
		{
			char character = lineText[endIndex];
			if (char.IsWhiteSpace(character) || character is ',' or ';' or ')' or '}' or ']' or '(' or '{' or '[')
			{
				break;
			}

			endIndex++;
		}

		return Math.Clamp(Math.Max(startColumn + 1, endIndex + 1), startColumn, lineMaxColumn);
	}

	private static string BuildDebugOutput(
		ForRestScriptCompilationResult compilation,
		string workspaceName,
		string environmentName,
		string requestName,
		string method,
		string target,
		string statusText,
		int requestVariableCount,
		int runtimeSeedCount,
		int testCount,
		int extractionCount,
		int sendBudget,
		int lineCount)
	{
		List<string> lines =
		[
			$"Workspace: {workspaceName}",
			$"Environment: {environmentName}",
			$"Request: {method} {requestName}".Trim(),
			$"Target: {target}",
			string.Empty,
			$"Compilation state: {statusText}",
			$"Document lines: {lineCount}",
			$"Request variables: {requestVariableCount}",
			$"Runtime seeds: {runtimeSeedCount}",
			$"Assertions: {testCount}",
			$"Extractions: {extractionCount}",
			$"Max request.send() iterations: {sendBudget}"
		];

		if (compilation.Diagnostics.Count > 0)
		{
			lines.Add(string.Empty);
			lines.Add("Compilation diagnostics:");
			lines.AddRange(
				compilation.Diagnostics.Select(
					diagnostic =>
					{
						int normalizedLine = NormalizeLine(diagnostic.Line, lineCount);
						int normalizedColumn = Math.Max(1, diagnostic.Column);
						return $"  [{diagnostic.Severity}] L{normalizedLine}:{normalizedColumn} {diagnostic.Message}";
					}));
		}

		return string.Join(Environment.NewLine, lines);
	}

	private static int NormalizeLine(int line, int lineCount)
	{
		return Math.Clamp(line <= 0 ? 1 : line, 1, Math.Max(1, lineCount));
	}

	private static string NormalizeLineEndings(string value)
	{
		return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
	}
}
