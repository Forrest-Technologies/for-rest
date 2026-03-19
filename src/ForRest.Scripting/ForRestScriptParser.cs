using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ForRest.Scripting;

public sealed class ForRestScriptParser
{
    #region Public Methods

    public ForRestScriptParseResult Parse(string source)
    {
        var diagnostics = new List<ForRestScriptDiagnostic>();
        var lines = Normalize(source).Split('\n');

        var meta = new Dictionary<string, ForRestScriptValueExpression>(StringComparer.OrdinalIgnoreCase);
        var request = new Dictionary<string, ForRestScriptValueExpression>(StringComparer.OrdinalIgnoreCase);
        var auth = new Dictionary<string, ForRestScriptValueExpression>(StringComparer.OrdinalIgnoreCase);
        var repeat = new Dictionary<string, ForRestScriptValueExpression>(StringComparer.OrdinalIgnoreCase);
        var retry = new Dictionary<string, ForRestScriptValueExpression>(StringComparer.OrdinalIgnoreCase);
        var variables = new List<ForRestScriptVariableDeclaration>();
        var query = new List<ForRestScriptNamedValue>();
        var headers = new List<ForRestScriptNamedValue>();
        var formValues = new List<ForRestScriptNamedValue>();
        var multipartValues = new List<ForRestScriptNamedValue>();
        var extractions = new List<ForRestScriptExtraction>();
        var tests = new List<ForRestScriptAssertion>();
        ForRestScriptBodySection? body = null;

        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || IsComment(trimmed))
            {
                index++;
                continue;
            }

            if (TryParseBody(lines, ref index, diagnostics, out var parsedBody))
            {
                body = parsedBody;
                continue;
            }

            if (!trimmed.EndsWith('{'))
            {
                diagnostics.Add(CreateDiagnostic($"Expected a section opening, but found '{trimmed}'.", index + 1, line));
                index++;
                continue;
            }

            var sectionName = trimmed[..^1].Trim().ToLowerInvariant();
            index++;

            switch (sectionName)
            {
                case "meta":
                    ParseKeyValueSection(lines, ref index, meta, diagnostics, sectionName);
                    break;
                case "vars":
                    ParseVariablesSection(lines, ref index, variables, diagnostics);
                    break;
                case "request":
                    ParseKeyValueSection(lines, ref index, request, diagnostics, sectionName);
                    break;
                case "auth":
                    ParseKeyValueSection(lines, ref index, auth, diagnostics, sectionName);
                    break;
                case "query":
                    ParseNamedValueSection(lines, ref index, query, diagnostics, sectionName);
                    break;
                case "headers":
                    ParseNamedValueSection(lines, ref index, headers, diagnostics, sectionName);
                    break;
                case "form":
                    ParseNamedValueSection(lines, ref index, formValues, diagnostics, sectionName);
                    break;
                case "multipart":
                    ParseNamedValueSection(lines, ref index, multipartValues, diagnostics, sectionName);
                    break;
                case "extract":
                    ParseExtractionSection(lines, ref index, extractions, diagnostics);
                    break;
                case "tests":
                    ParseTestsSection(lines, ref index, tests, diagnostics);
                    break;
                case "repeat":
                    ParseKeyValueSection(lines, ref index, repeat, diagnostics, sectionName);
                    break;
                case "retry":
                    ParseKeyValueSection(lines, ref index, retry, diagnostics, sectionName);
                    break;
                default:
                    diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, $"Unknown section '{sectionName}'.", index, 1));
                    SkipSection(lines, ref index);
                    break;
            }
        }

        if (diagnostics.Any(static item => item.Severity == ForRestScriptDiagnosticSeverity.Error))
        {
            return new(null, diagnostics);
        }

        return new(
            new()
            {
                Meta = meta,
                Variables = variables,
                Request = request,
                Auth = auth,
                QueryParameters = query,
                Headers = headers,
                Body = body,
                FormValues = formValues,
                MultipartValues = multipartValues,
                Extractions = extractions,
                Tests = tests,
                Repeat = repeat,
                Retry = retry,
            },
            diagnostics);
    }

    #endregion

    #region Private Methods

    private static string Normalize(string source)
    {
        return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static bool IsComment(string trimmedLine)
    {
        return trimmedLine.StartsWith('#');
    }

    private static bool TryParseBody(
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        out ForRestScriptBodySection? body)
    {
        body = null;
        var line = lines[index];
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("body ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = trimmed[5..].Trim();
        var delimiterIndex = remainder.IndexOf("\"\"\"", StringComparison.Ordinal);
        if (delimiterIndex < 0)
        {
            diagnostics.Add(CreateDiagnostic("The body section must declare a mode followed by an opening triple quote.", index + 1, line));
            index++;
            return true;
        }

        var modeText = remainder[..delimiterIndex].Trim();
        if (!TryParseBodyMode(modeText, out var mode))
        {
            diagnostics.Add(CreateDiagnostic($"Unknown body mode '{modeText}'.", index + 1, line));
            index++;
            return true;
        }

        var contentBuilder = new List<string>();
        var trailing = remainder[(delimiterIndex + 3)..];
        if (!string.IsNullOrEmpty(trailing))
        {
            contentBuilder.Add(trailing);
        }

        index++;
        var terminated = false;
        while (index < lines.Count)
        {
            var bodyLine = lines[index];
            var closeIndex = bodyLine.IndexOf("\"\"\"", StringComparison.Ordinal);
            if (closeIndex >= 0)
            {
                contentBuilder.Add(bodyLine[..closeIndex]);
                terminated = true;
                index++;
                break;
            }

            contentBuilder.Add(bodyLine);
            index++;
        }

        if (!terminated)
        {
            diagnostics.Add(CreateDiagnostic("The body section is missing a closing triple quote.", index, line));
            return true;
        }

        body = new(mode, string.Join(Environment.NewLine, contentBuilder));
        return true;
    }

    private static void ParseKeyValueSection(
        IReadOnlyList<string> lines,
        ref int index,
        Dictionary<string, ForRestScriptValueExpression> target,
        List<ForRestScriptDiagnostic> diagnostics,
        string sectionName)
    {
        while (index < lines.Count)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || IsComment(trimmed))
            {
                index++;
                continue;
            }

            if (trimmed == "}")
            {
                index++;
                return;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
            {
                diagnostics.Add(CreateDiagnostic($"Expected a key/value assignment inside '{sectionName}'.", index + 1, line));
                index++;
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var rawValue = trimmed[(separatorIndex + 1)..].Trim();
            if (!TryParseExpression(rawValue, out var expression))
            {
                diagnostics.Add(CreateDiagnostic($"Could not parse the value '{rawValue}' inside '{sectionName}'.", index + 1, line));
                index++;
                continue;
            }

            target[key] = expression!;
            index++;
        }

        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, $"The '{sectionName}' section is missing a closing '}}'.", index, 1));
    }

    private static void ParseVariablesSection(
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptVariableDeclaration> variables,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        while (index < lines.Count)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || IsComment(trimmed))
            {
                index++;
                continue;
            }

            if (trimmed == "}")
            {
                index++;
                return;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
            {
                diagnostics.Add(CreateDiagnostic("Variable declarations must use '<scope> <name> = <value>'.", index + 1, line));
                index++;
                continue;
            }

            var left = trimmed[..separatorIndex].Trim();
            var rawValue = trimmed[(separatorIndex + 1)..].Trim();
            var leftParts = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (leftParts.Length != 2 || !TryParseVariableScope(leftParts[0], out var scope))
            {
                diagnostics.Add(CreateDiagnostic("Variable declarations must start with 'request' or 'runtime'.", index + 1, line));
                index++;
                continue;
            }

            if (!TryParseExpression(rawValue, out var expression))
            {
                diagnostics.Add(CreateDiagnostic($"Could not parse the value '{rawValue}' for variable '{leftParts[1]}'.", index + 1, line));
                index++;
                continue;
            }

            variables.Add(new(scope, leftParts[1], expression!));
            index++;
        }

        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "The 'vars' section is missing a closing '}'.", index, 1));
    }

    private static void ParseNamedValueSection(
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptNamedValue> values,
        List<ForRestScriptDiagnostic> diagnostics,
        string sectionName)
    {
        while (index < lines.Count)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || IsComment(trimmed))
            {
                index++;
                continue;
            }

            if (trimmed == "}")
            {
                index++;
                return;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
            {
                diagnostics.Add(CreateDiagnostic($"Entries in '{sectionName}' must use '<name> = <value>'.", index + 1, line));
                index++;
                continue;
            }

            var rawKey = trimmed[..separatorIndex].Trim();
            var rawValue = trimmed[(separatorIndex + 1)..].Trim();
            var key = TryParseQuotedString(rawKey, out var stringKey)
                ? stringKey!
                : rawKey;

            if (!TryParseExpression(rawValue, out var expression))
            {
                diagnostics.Add(CreateDiagnostic($"Could not parse the value '{rawValue}' for '{key}'.", index + 1, line));
                index++;
                continue;
            }

            values.Add(new(key, expression!));
            index++;
        }

        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, $"The '{sectionName}' section is missing a closing '}}'.", index, 1));
    }

    private static void ParseExtractionSection(
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptExtraction> extractions,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        while (index < lines.Count)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || IsComment(trimmed))
            {
                index++;
                continue;
            }

            if (trimmed == "}")
            {
                index++;
                return;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
            {
                diagnostics.Add(CreateDiagnostic("Extraction declarations must use '<scope> <name> = json \"$.path\"'.", index + 1, line));
                index++;
                continue;
            }

            var left = trimmed[..separatorIndex].Trim();
            var right = trimmed[(separatorIndex + 1)..].Trim();
            var leftParts = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (leftParts.Length != 2 || !TryParseExtractionScope(leftParts[0], out var targetScope))
            {
                diagnostics.Add(CreateDiagnostic("Extraction targets must use 'runtime' or 'request'.", index + 1, line));
                index++;
                continue;
            }

            if (!right.StartsWith("json ", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(CreateDiagnostic("Extraction selectors currently support only the 'json' selector type.", index + 1, line));
                index++;
                continue;
            }

            var rawSelector = right[5..].Trim();
            if (!TryParseQuotedString(rawSelector, out var selector))
            {
                diagnostics.Add(CreateDiagnostic("Extraction selectors must be quoted JSON selector strings.", index + 1, line));
                index++;
                continue;
            }

            extractions.Add(new(targetScope, leftParts[1], selector!));
            index++;
        }

        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "The 'extract' section is missing a closing '}'.", index, 1));
    }

    private static void ParseTestsSection(
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptAssertion> tests,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        while (index < lines.Count)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || IsComment(trimmed))
            {
                index++;
                continue;
            }

            if (trimmed == "}")
            {
                index++;
                return;
            }

            if (TryParseStatusAssertion(trimmed, out var parsedAssertion)
                || TryParseBodyAssertion(trimmed, out parsedAssertion)
                || TryParseHeaderAssertion(trimmed, out parsedAssertion)
                || TryParseJsonAssertion(trimmed, out parsedAssertion))
            {
                tests.Add(parsedAssertion!);
                index++;
                continue;
            }

            diagnostics.Add(CreateDiagnostic("Could not parse the test assertion.", index + 1, line));
            index++;
        }

        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "The 'tests' section is missing a closing '}'.", index, 1));
    }

    private static bool TryParseStatusAssertion(string trimmedLine, out ForRestScriptAssertion? assertion)
    {
        assertion = null;
        if (!trimmedLine.StartsWith("status ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryReadMessageAssertion(trimmedLine[7..].Trim(), out var expressionText, out var message)
            || !TryReadLeadingOperator(expressionText, out var comparisonOperator, out var right)
            || !TryParseExpression(right, out var value))
        {
            return false;
        }

        assertion = new(ForRestScriptAssertionTarget.Status, comparisonOperator, message!, Value: value);
        return true;
    }

    private static bool TryParseBodyAssertion(string trimmedLine, out ForRestScriptAssertion? assertion)
    {
        assertion = null;
        if (!trimmedLine.StartsWith("body ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryReadMessageAssertion(trimmedLine[5..].Trim(), out var expressionText, out var message)
            || !TryReadLeadingOperator(expressionText, out var comparisonOperator, out var right)
            || !TryParseExpression(right, out var value))
        {
            return false;
        }

        assertion = new(ForRestScriptAssertionTarget.Body, comparisonOperator, message!, Value: value);
        return true;
    }

    private static bool TryParseHeaderAssertion(string trimmedLine, out ForRestScriptAssertion? assertion)
    {
        assertion = null;
        if (!trimmedLine.StartsWith("header ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = trimmedLine[7..].Trim();
        if (!TryReadQuotedToken(remainder, out var headerName, out var afterHeader))
        {
            return false;
        }

        if (!TryReadMessageAssertion(afterHeader.Trim(), out var expressionText, out var message)
            || !TryReadLeadingOperator(expressionText, out var comparisonOperator, out var right)
            || !TryParseExpression(right, out var value))
        {
            return false;
        }

        assertion = new(ForRestScriptAssertionTarget.Header, comparisonOperator, message!, HeaderName: headerName, Value: value);
        return true;
    }

    private static bool TryParseJsonAssertion(string trimmedLine, out ForRestScriptAssertion? assertion)
    {
        assertion = null;
        if (!trimmedLine.StartsWith("json ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = trimmedLine[5..].Trim();
        if (!TryReadQuotedToken(remainder, out var selector, out var afterSelector))
        {
            return false;
        }

        if (!TryReadMessageAssertion(afterSelector.Trim(), out var expressionText, out var message))
        {
            return false;
        }

        if (string.Equals(expressionText, "exists", StringComparison.OrdinalIgnoreCase))
        {
            assertion = new(ForRestScriptAssertionTarget.Json, ForRestScriptComparisonOperator.Exists, message!, Selector: selector);
            return true;
        }

        if (!TryReadLeadingOperator(expressionText, out var comparisonOperator, out var right)
            || !TryParseExpression(right, out var value))
        {
            return false;
        }

        assertion = new(ForRestScriptAssertionTarget.Json, comparisonOperator, message!, Selector: selector, Value: value);
        return true;
    }

    private static bool TryReadMessageAssertion(string text, out string expressionText, out string? message)
    {
        expressionText = string.Empty;
        message = null;
        var lastQuoteIndex = text.LastIndexOf('"');
        if (lastQuoteIndex < 0)
        {
            return false;
        }

        var startQuoteIndex = text.LastIndexOf('"', lastQuoteIndex - 1);
        if (startQuoteIndex < 0)
        {
            return false;
        }

        var rawMessage = text[startQuoteIndex..];
        if (!TryParseQuotedString(rawMessage, out message))
        {
            return false;
        }

        expressionText = text[..startQuoteIndex].Trim();
        return !string.IsNullOrWhiteSpace(expressionText);
    }

    private static bool TryReadQuotedToken(string text, out string value, out string remainder)
    {
        value = string.Empty;
        remainder = string.Empty;
        if (!text.StartsWith('"'))
        {
            return false;
        }

        var escaped = false;
        for (var index = 1; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"' && !escaped)
            {
                var raw = text[..(index + 1)];
                if (!TryParseQuotedString(raw, out var parsed))
                {
                    return false;
                }

                value = parsed!;
                remainder = index + 1 < text.Length ? text[(index + 1)..] : string.Empty;
                return true;
            }

            escaped = character == '\\' && !escaped;
            if (character != '\\')
            {
                escaped = false;
            }
        }

        return false;
    }

    private static bool TrySplitOperator(
        string text,
        out string left,
        out ForRestScriptComparisonOperator comparisonOperator,
        out string right)
    {
        foreach (var candidate in new[]
                 {
                     (" contains ", ForRestScriptComparisonOperator.Contains),
                     (" == ", ForRestScriptComparisonOperator.Equal),
                     (" != ", ForRestScriptComparisonOperator.NotEqual),
                     (" >= ", ForRestScriptComparisonOperator.GreaterThanOrEqual),
                     (" <= ", ForRestScriptComparisonOperator.LessThanOrEqual),
                     (" > ", ForRestScriptComparisonOperator.GreaterThan),
                     (" < ", ForRestScriptComparisonOperator.LessThan),
                 })
        {
            var separatorIndex = text.IndexOf(candidate.Item1, StringComparison.OrdinalIgnoreCase);
            if (separatorIndex < 0)
            {
                continue;
            }

            left = text[..separatorIndex].Trim();
            right = text[(separatorIndex + candidate.Item1.Length)..].Trim();
            comparisonOperator = candidate.Item2;
            return true;
        }

        left = string.Empty;
        right = string.Empty;
        comparisonOperator = ForRestScriptComparisonOperator.Equal;
        return false;
    }

    private static bool TryReadLeadingOperator(
        string text,
        out ForRestScriptComparisonOperator comparisonOperator,
        out string right)
    {
        foreach (var candidate in new[]
                 {
                     ("contains ", ForRestScriptComparisonOperator.Contains),
                     ("== ", ForRestScriptComparisonOperator.Equal),
                     ("!= ", ForRestScriptComparisonOperator.NotEqual),
                     (">= ", ForRestScriptComparisonOperator.GreaterThanOrEqual),
                     ("<= ", ForRestScriptComparisonOperator.LessThanOrEqual),
                     ("> ", ForRestScriptComparisonOperator.GreaterThan),
                     ("< ", ForRestScriptComparisonOperator.LessThan),
                 })
        {
            if (!text.StartsWith(candidate.Item1, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            comparisonOperator = candidate.Item2;
            right = text[candidate.Item1.Length..].Trim();
            return true;
        }

        comparisonOperator = ForRestScriptComparisonOperator.Equal;
        right = string.Empty;
        return false;
    }

    private static bool TryParseExpression(string rawValue, out ForRestScriptValueExpression? expression)
    {
        expression = null;
        var trimmed = rawValue.Trim();
        if (TryParseQuotedString(trimmed, out var stringValue))
        {
            expression = new ForRestScriptStringExpression(stringValue!);
            return true;
        }

        if (bool.TryParse(trimmed, out var boolValue))
        {
            expression = new ForRestScriptBooleanExpression(boolValue);
            return true;
        }

        if (int.TryParse(trimmed, out var intValue))
        {
            expression = new ForRestScriptNumberExpression(intValue);
            return true;
        }

        var openParenIndex = trimmed.IndexOf('(');
        if (openParenIndex > 0 && trimmed.EndsWith(')'))
        {
            var functionName = trimmed[..openParenIndex].Trim();
            var rawArguments = trimmed[(openParenIndex + 1)..^1];
            var arguments = new List<ForRestScriptValueExpression>();

            foreach (var argument in SplitArguments(rawArguments))
            {
                if (!TryParseExpression(argument, out var argumentExpression) || argumentExpression is null)
                {
                    return false;
                }

                arguments.Add(argumentExpression);
            }

            expression = new ForRestScriptFunctionCallExpression(functionName, arguments);
            return true;
        }

        if (Regex.IsMatch(trimmed, @"^[A-Za-z_][A-Za-z0-9_\-\.]*$"))
        {
            expression = new ForRestScriptIdentifierExpression(trimmed);
            return true;
        }

        return false;
    }

    private static List<string> SplitArguments(string rawArguments)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return arguments;
        }

        var current = new StringBuilder();
        var depth = 0;
        var inString = false;
        var escaped = false;

        foreach (var character in rawArguments)
        {
            if (character == '"' && !escaped)
            {
                inString = !inString;
            }

            if (!inString)
            {
                if (character == '(')
                {
                    depth++;
                }
                else if (character == ')')
                {
                    depth--;
                }
                else if (character == ',' && depth == 0)
                {
                    arguments.Add(current.ToString().Trim());
                    current.Clear();
                    escaped = false;
                    continue;
                }
            }

            current.Append(character);
            escaped = character == '\\' && !escaped;
            if (character != '\\')
            {
                escaped = false;
            }
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString().Trim());
        }

        return arguments;
    }

    private static bool TryParseQuotedString(string rawValue, out string? value)
    {
        value = null;
        if (!rawValue.StartsWith('"') || !rawValue.EndsWith('"'))
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<string>(rawValue);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseVariableScope(string rawValue, out ForRestScriptVariableScope scope)
    {
        switch (rawValue.Trim().ToLowerInvariant())
        {
            case "request":
                scope = ForRestScriptVariableScope.Request;
                return true;
            case "runtime":
                scope = ForRestScriptVariableScope.Runtime;
                return true;
            default:
                scope = ForRestScriptVariableScope.Request;
                return false;
        }
    }

    private static bool TryParseExtractionScope(string rawValue, out VariableScope scope)
    {
        switch (rawValue.Trim().ToLowerInvariant())
        {
            case "request":
                scope = VariableScope.RequestLocal;
                return true;
            case "runtime":
                scope = VariableScope.Runtime;
                return true;
            default:
                scope = VariableScope.Runtime;
                return false;
        }
    }

    private static bool TryParseBodyMode(string rawValue, out RequestBodyMode mode)
    {
        switch (rawValue.Trim().ToLowerInvariant())
        {
            case "json":
                mode = RequestBodyMode.Json;
                return true;
            case "raw":
            case "text":
                mode = RequestBodyMode.RawText;
                return true;
            default:
                mode = RequestBodyMode.None;
                return false;
        }
    }

    private static void SkipSection(IReadOnlyList<string> lines, ref int index)
    {
        while (index < lines.Count)
        {
            if (lines[index].Trim() == "}")
            {
                index++;
                return;
            }

            index++;
        }
    }

    private static ForRestScriptDiagnostic CreateDiagnostic(string message, int lineNumber, string sourceLine)
    {
        var column = string.IsNullOrWhiteSpace(sourceLine)
            ? 1
            : sourceLine.TakeWhile(static character => char.IsWhiteSpace(character)).Count() + 1;

        return new(ForRestScriptDiagnosticSeverity.Error, message, lineNumber, column);
    }

    #endregion
}
