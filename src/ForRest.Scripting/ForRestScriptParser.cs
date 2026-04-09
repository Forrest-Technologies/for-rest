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
        var flowLines = new List<string>();
        var handlers = new List<ForRestScriptHandler>();
        var scenarios = new List<ForRestScriptScenario>();
        var imports = new List<string>();
        ForRestScriptBodySection? body = null;

        var index = 0;
        var rawFlowDepth = 0;
        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || IsComment(trimmed))
            {
                index++;
                continue;
            }

            if (rawFlowDepth > 0)
            {
                flowLines.Add(line);
                rawFlowDepth = Math.Max(0, rawFlowDepth + CountBraceDelta(line));
                index++;
                continue;
            }

            if (TryParseImportDirective(trimmed, out var importPath))
            {
                imports.Add(importPath);
                index++;
                continue;
            }

            if (TryParseBody(lines, ref index, diagnostics, out var parsedBody))
            {
                body = parsedBody;
                continue;
            }

            if (TryParseFlow(lines, ref index, diagnostics, out var parsedFlow))
            {
                AppendFlowLines(flowLines, parsedFlow);
                continue;
            }

            if (TryParseOnHandler(lines, ref index, trimmed, diagnostics, out var handler))
            {
                handlers.Add(handler!);
                continue;
            }

            if (TryParseScenarioSection(lines, ref index, trimmed, diagnostics, out var scenario))
            {
                scenarios.Add(scenario!);
                continue;
            }

            if (TryGetKnownSectionName(trimmed, out var sectionName))
            {
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
                }

                continue;
            }

            if (TryParseTopLevelMeta(trimmed, index + 1, line, meta, diagnostics)
                || TryParseTopLevelRequest(trimmed, index + 1, line, request, diagnostics)
                || TryParseTopLevelVariable(trimmed, index + 1, line, variables, diagnostics)
                || TryParseTopLevelKeyValue(trimmed, index + 1, line, "auth", auth, diagnostics)
                || TryParseTopLevelNamedValue(trimmed, index + 1, line, "header", headers, diagnostics)
                || TryParseTopLevelNamedValue(trimmed, index + 1, line, "query", query, diagnostics)
                || TryParseTopLevelNamedValue(trimmed, index + 1, line, "form", formValues, diagnostics)
                || TryParseTopLevelNamedValue(trimmed, index + 1, line, "multipart", multipartValues, diagnostics)
                || TryParseTopLevelExtraction(trimmed, index + 1, line, extractions, diagnostics)
                || TryParseTopLevelAssertion(trimmed, index + 1, line, tests, diagnostics)
                || TryParseTopLevelKeyValue(trimmed, index + 1, line, "repeat", repeat, diagnostics)
                || (!IsFlowRetryLine(trimmed) && TryParseTopLevelKeyValue(trimmed, index + 1, line, "retry", retry, diagnostics)))
            {
                index++;
                continue;
            }

            flowLines.Add(line);
            rawFlowDepth = Math.Max(0, CountBraceDelta(line));
            index++;
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
                Flow = string.Join(Environment.NewLine, flowLines).Trim(),
                Tests = tests,
                Repeat = repeat,
                Retry = retry,
                Handlers = handlers,
                Scenarios = scenarios,
                Imports = imports,
            },
            diagnostics);
    }

    #endregion

    #region Private Methods

    private static void AppendFlowLines(List<string> flowLines, string? flow)
    {
        if (string.IsNullOrWhiteSpace(flow))
        {
            return;
        }

        flowLines.AddRange(Normalize(flow).Split('\n'));
    }

    private static bool TryParseImportDirective(string trimmed, out string path)
    {
        path = string.Empty;
        string? remainder = null;

        if (trimmed.StartsWith("import ", StringComparison.Ordinal))
        {
            remainder = trimmed["import ".Length..].Trim();
        }
        else if (trimmed.StartsWith("use ", StringComparison.Ordinal))
        {
            remainder = trimmed["use ".Length..].Trim();
        }

        if (remainder is null)
        {
            return false;
        }

        if (remainder.Length >= 2 && remainder[0] == '"' && remainder[^1] == '"')
        {
            path = remainder[1..^1];
            return !string.IsNullOrWhiteSpace(path);
        }

        return false;
    }

    private static bool TryParseOnHandler(
        string[] lines,
        ref int index,
        string trimmed,
        List<ForRestScriptDiagnostic> diagnostics,
        out ForRestScriptHandler? handler)
    {
        handler = null;
        if (!trimmed.StartsWith("on ", StringComparison.Ordinal))
        {
            return false;
        }

        var afterOn = trimmed["on ".Length..].Trim();
        ForRestScriptHandlerKind kind;
        int? statusCode = null;

        if (afterOn.StartsWith("error", StringComparison.OrdinalIgnoreCase))
        {
            var rest = afterOn["error".Length..].Trim();
            if (rest != "{")
            {
                return false;
            }

            kind = ForRestScriptHandlerKind.OnError;
        }
        else if (afterOn.StartsWith("status", StringComparison.OrdinalIgnoreCase))
        {
            var rest = afterOn["status".Length..].Trim();
            var braceIdx = rest.IndexOf('{');
            if (braceIdx < 0)
            {
                return false;
            }

            var codeText = rest[..braceIdx].Trim();
            if (!int.TryParse(codeText, out var code))
            {
                diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, $"Invalid status code '{codeText}' in on status handler.", index + 1, 1));
                return false;
            }

            statusCode = code;
            kind = ForRestScriptHandlerKind.OnStatus;
        }
        else
        {
            return false;
        }

        index++;
        var bodyLines = new List<string>();
        var depth = 1;
        while (index < lines.Length && depth > 0)
        {
            var line = lines[index];
            var lineTrimmed = line.Trim();
            depth += CountBraceDelta(line);
            if (depth <= 0)
            {
                index++;
                break;
            }

            bodyLines.Add(line);
            index++;
        }

        handler = new(kind, statusCode, string.Join(Environment.NewLine, bodyLines).Trim());
        return true;
    }

    private static bool TryParseScenarioSection(
        string[] lines,
        ref int index,
        string trimmed,
        List<ForRestScriptDiagnostic> diagnostics,
        out ForRestScriptScenario? scenario)
    {
        scenario = null;
        if (!trimmed.StartsWith("scenario ", StringComparison.Ordinal))
        {
            return false;
        }

        var afterScenario = trimmed["scenario ".Length..].Trim();
        var nameEndIdx = afterScenario.IndexOf('"', 1);
        if (!afterScenario.StartsWith('"') || nameEndIdx < 0)
        {
            return false;
        }

        var name = afterScenario[1..nameEndIdx];
        var rest = afterScenario[(nameEndIdx + 1)..].Trim();
        if (rest != "{")
        {
            return false;
        }

        index++;

        var scenarioAuth = new Dictionary<string, ForRestScriptValueExpression>(StringComparer.OrdinalIgnoreCase);
        var scenarioHeaders = new List<ForRestScriptNamedValue>();
        var scenarioTests = new List<ForRestScriptAssertion>();
        var scenarioFlowLines = new List<string>();
        var depth = 1;

        while (index < lines.Length && depth > 0)
        {
            var line = lines[index];
            var lineTrimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(lineTrimmed) || IsComment(lineTrimmed))
            {
                index++;
                continue;
            }

            depth += CountBraceDelta(line);
            if (depth <= 0)
            {
                index++;
                break;
            }

            if (TryGetKnownSectionName(lineTrimmed, out var sectionName) && sectionName == "auth")
            {
                index++;
                ParseKeyValueSection(lines, ref index, scenarioAuth, diagnostics, "auth");
                continue;
            }

            if (TryGetKnownSectionName(lineTrimmed, out sectionName) && sectionName == "headers")
            {
                index++;
                ParseNamedValueSection(lines, ref index, scenarioHeaders, diagnostics, "headers");
                continue;
            }

            if (TryParseTopLevelKeyValue(lineTrimmed, index + 1, line, "auth", scenarioAuth, diagnostics))
            {
                index++;
                continue;
            }

            if (TryParseTopLevelNamedValue(lineTrimmed, index + 1, line, "header", scenarioHeaders, diagnostics))
            {
                index++;
                continue;
            }

            if (TryParseTopLevelAssertion(lineTrimmed, index + 1, line, scenarioTests, diagnostics))
            {
                index++;
                continue;
            }

            scenarioFlowLines.Add(line);
            index++;
        }

        scenario = new(
            name,
            scenarioAuth,
            scenarioHeaders,
            scenarioTests,
            string.Join(Environment.NewLine, scenarioFlowLines).Trim());

        return true;
    }

    private static string Normalize(string source)
    {
        return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static bool IsComment(string trimmedLine)
    {
        return trimmedLine.StartsWith('#');
    }

    private static bool TryGetKnownSectionName(string trimmed, out string sectionName)
    {
        sectionName = string.Empty;
        if (!trimmed.EndsWith('{'))
        {
            return false;
        }

        string candidate = trimmed[..^1].Trim().ToLowerInvariant();
        if (candidate is not ("meta" or "vars" or "request" or "auth" or "query" or "headers" or "form" or "multipart" or "extract" or "tests" or "repeat" or "retry"))
        {
            return false;
        }

        sectionName = candidate;
        return true;
    }

    private static bool TryParseTopLevelMeta(
        string trimmed,
        int lineNumber,
        string sourceLine,
        Dictionary<string, ForRestScriptValueExpression> meta,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        return TryParseTopLevelDirective(trimmed, lineNumber, sourceLine, "name", "name", meta, diagnostics);
    }

    private static bool TryParseTopLevelRequest(
        string trimmed,
        int lineNumber,
        string sourceLine,
        Dictionary<string, ForRestScriptValueExpression> request,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        foreach ((string Alias, string Key) mapping in new[]
                 {
                     ("method", "method"),
                     ("url", "url"),
                     ("timeout", "timeout"),
                     ("max_send_iterations", "max_send_iterations"),
                     ("redirects", "redirects"),
                     ("ssl", "ssl"),
                     ("history", "history"),
                     ("content_type", "content_type"),
                     ("user_agent", "user_agent"),
                     ("custom_user_agent", "custom_user_agent"),
                     ("request.user_agent", "user_agent"),
                     ("request.timeout", "timeout"),
                     ("request.max_send_iterations", "max_send_iterations"),
                     ("request.redirects", "redirects"),
                     ("request.ssl", "ssl"),
                     ("request.history", "history"),
                 })
        {
            if (TryParseTopLevelDirective(trimmed, lineNumber, sourceLine, mapping.Alias, mapping.Key, request, diagnostics))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseTopLevelDirective(
        string trimmed,
        int lineNumber,
        string sourceLine,
        string alias,
        string key,
        Dictionary<string, ForRestScriptValueExpression> target,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        if (!TryReadDirectiveValue(trimmed, alias, out var rawValue))
        {
            return false;
        }

        if (!TryParseExpression(rawValue, out var expression))
        {
            diagnostics.Add(CreateDiagnostic($"Could not parse the value '{rawValue}' for '{alias}'.", lineNumber, sourceLine));
            return true;
        }

        target[key] = expression!;
        return true;
    }

    private static bool TryParseTopLevelVariable(
        string trimmed,
        int lineNumber,
        string sourceLine,
        List<ForRestScriptVariableDeclaration> variables,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        if (!TryParseVariableDeclaration(trimmed, out var declaration, out var errorMessage))
        {
            return false;
        }

        if (declaration is null)
        {
            diagnostics.Add(CreateDiagnostic(errorMessage ?? "Invalid variable declaration.", lineNumber, sourceLine));
            return true;
        }

        variables.Add(declaration);
        return true;
    }

    private static bool TryParseTopLevelNamedValue(
        string trimmed,
        int lineNumber,
        string sourceLine,
        string keyword,
        List<ForRestScriptNamedValue> target,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        if (!TryParseNamedValueDirective(trimmed, keyword, out var value, out var errorMessage))
        {
            return false;
        }

        if (value is null)
        {
            diagnostics.Add(CreateDiagnostic(errorMessage ?? $"Invalid '{keyword}' directive.", lineNumber, sourceLine));
            return true;
        }

        target.Add(value);
        return true;
    }

    private static bool TryParseTopLevelExtraction(
        string trimmed,
        int lineNumber,
        string sourceLine,
        List<ForRestScriptExtraction> extractions,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        if (!trimmed.StartsWith("extract ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryParseExtractionDirective(trimmed[8..].Trim(), out var extraction, out var errorMessage))
        {
            diagnostics.Add(CreateDiagnostic(errorMessage ?? "Invalid extract directive.", lineNumber, sourceLine));
            return true;
        }

        extractions.Add(extraction!);
        return true;
    }

    private static bool TryParseTopLevelAssertion(
        string trimmed,
        int lineNumber,
        string sourceLine,
        List<ForRestScriptAssertion> tests,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        if (!trimmed.StartsWith("expect ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string assertionText = trimmed[7..].Trim();
        if (TryParseStatusAssertion(assertionText, out var assertion)
            || TryParseBodyAssertion(assertionText, out assertion)
            || TryParseHeaderAssertion(assertionText, out assertion)
            || TryParseJsonAssertion(assertionText, out assertion))
        {
            tests.Add(assertion!);
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            "Could not parse the expectation. Use forms like 'expect status == 200 \"returns 200\"', 'expect header \"Content-Type\" contains \"json\" \"json response\"', or 'expect json \"$.id\" exists \"has id\"'.",
            lineNumber,
            sourceLine));
        return true;
    }

    private static bool TryParseTopLevelKeyValue(
        string trimmed,
        int lineNumber,
        string sourceLine,
        string keyword,
        Dictionary<string, ForRestScriptValueExpression> target,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        if (!trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string remainder = trimmed[(keyword.Length + 1)..].Trim();
        int separatorIndex = remainder.IndexOf('=');
        if (separatorIndex < 0)
        {
            diagnostics.Add(CreateDiagnostic($"The '{keyword}' directive must use '<name> = <value>'.", lineNumber, sourceLine));
            return true;
        }

        string key = remainder[..separatorIndex].Trim();
        string rawValue = TrimOptionalTerminator(remainder[(separatorIndex + 1)..]);
        if (!TryParseExpression(rawValue, out var expression))
        {
            diagnostics.Add(CreateDiagnostic($"Could not parse the value '{rawValue}' for '{keyword} {key}'.", lineNumber, sourceLine));
            return true;
        }

        target[key] = expression!;
        return true;
    }

    private static bool IsFlowRetryLine(string trimmed)
    {
        if (!trimmed.StartsWith("retry ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string remainder = trimmed[6..].TrimStart();
        return remainder.Length > 0 && char.IsDigit(remainder[0]);
    }

    private static bool TryReadDirectiveValue(string trimmed, string directive, out string rawValue)
    {
        rawValue = string.Empty;
        if (!trimmed.StartsWith(directive, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string remainder = trimmed[directive.Length..];
        if (remainder.Length == 0)
        {
            return false;
        }

        char first = remainder[0];
        if (!char.IsWhiteSpace(first) && first != '=')
        {
            return false;
        }

        rawValue = TrimOptionalTerminator(remainder);
        if (rawValue.StartsWith("="))
        {
            rawValue = TrimOptionalTerminator(rawValue[1..]);
        }

        return !string.IsNullOrWhiteSpace(rawValue);
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
            var rawValue = TrimOptionalTerminator(trimmed[(separatorIndex + 1)..]);
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

            if (!TryParseVariableDeclaration(trimmed, out var declaration, out var errorMessage))
            {
                diagnostics.Add(CreateDiagnostic(errorMessage ?? "Variable declarations must start with 'request' or 'runtime'.", index + 1, line));
                index++;
                continue;
            }

            variables.Add(declaration!);
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

            if (!TryParseNamedValueDirective(trimmed, string.Empty, out var namedValue, out var errorMessage))
            {
                diagnostics.Add(CreateDiagnostic(errorMessage ?? $"Could not parse the value for '{sectionName}'.", index + 1, line));
                index++;
                continue;
            }

            values.Add(namedValue!);
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

            if (!TryParseExtractionDirective(trimmed, out var extraction, out var errorMessage))
            {
                diagnostics.Add(CreateDiagnostic(errorMessage ?? "Invalid extraction directive.", index + 1, line));
                index++;
                continue;
            }

            extractions.Add(extraction!);
            index++;
        }

        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "The 'extract' section is missing a closing '}'.", index, 1));
    }

    private static bool TryParseVariableDeclaration(
        string trimmed,
        out ForRestScriptVariableDeclaration? declaration,
        out string? errorMessage)
    {
        declaration = null;
        errorMessage = null;
        int separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex < 0)
        {
            return false;
        }

        string left = trimmed[..separatorIndex].Trim();
        string rawValue = TrimOptionalTerminator(trimmed[(separatorIndex + 1)..]);
        string[] leftParts = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (leftParts.Length != 2 || !TryParseVariableScope(leftParts[0], out var scope))
        {
            return false;
        }

        if (!IsFlowSafeVariableName(leftParts[1]))
        {
            errorMessage = $"Variable '{leftParts[1]}' is not a valid ForRest identifier. Use letters, numbers, and underscores only.";
            return true;
        }

        if (!TryParseExpression(rawValue, out var expression))
        {
            errorMessage = $"Could not parse the value '{rawValue}' for variable '{leftParts[1]}'.";
            return true;
        }

        declaration = new(scope, leftParts[1], expression!);
        return true;
    }

    private static bool TryParseNamedValueDirective(
        string trimmed,
        string keyword,
        out ForRestScriptNamedValue? namedValue,
        out string? errorMessage)
    {
        namedValue = null;
        errorMessage = null;

        string remainder = trimmed;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            if (!trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            remainder = trimmed[(keyword.Length + 1)..].Trim();
        }

        int separatorIndex = remainder.IndexOf('=');
        if (separatorIndex < 0)
        {
            errorMessage = $"Entries in '{keyword}' must use '<name> = <value>'.";
            return true;
        }

        string rawKey = remainder[..separatorIndex].Trim();
        string rawValue = TrimOptionalTerminator(remainder[(separatorIndex + 1)..]);
        string key = TryParseQuotedString(rawKey, out var stringKey)
            ? stringKey!
            : rawKey;

        if (!TryParseExpression(rawValue, out var expression))
        {
            errorMessage = $"Could not parse the value '{rawValue}' for '{key}'.";
            return true;
        }

        namedValue = new(key, expression!);
        return true;
    }

    private static bool TryParseExtractionDirective(
        string trimmed,
        out ForRestScriptExtraction? extraction,
        out string? errorMessage)
    {
        extraction = null;
        errorMessage = null;

        int separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex < 0)
        {
            errorMessage = "Extraction declarations must use '<scope> <name> = json \"$.path\"'.";
            return false;
        }

        string left = trimmed[..separatorIndex].Trim();
        string right = TrimOptionalTerminator(trimmed[(separatorIndex + 1)..]);
        string[] leftParts = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (leftParts.Length != 2 || !TryParseExtractionScope(leftParts[0], out var targetScope))
        {
            errorMessage = "Extraction targets must use 'runtime' or 'request'.";
            return false;
        }

        if (!IsFlowSafeVariableName(leftParts[1]))
        {
            errorMessage = $"Variable '{leftParts[1]}' is not a valid ForRest identifier. Use letters, numbers, and underscores only.";
            return false;
        }

        if (right.StartsWith("json ", StringComparison.OrdinalIgnoreCase))
        {
            string rawSelector = TrimOptionalTerminator(right[5..]);
            if (!TryParseQuotedString(rawSelector, out var selector))
            {
                errorMessage = "Extraction selectors must be quoted JSON selector strings.";
                return false;
            }

            extraction = new(targetScope, leftParts[1], ForRestScriptExtractionSource.Json, selector!);
            return true;
        }

        if (!right.StartsWith("regex ", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Extraction selectors currently support 'json' and 'regex'.";
            return false;
        }

        var regexRemainder = right[6..].Trim();
        if (regexRemainder.StartsWith("body ", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRegexExtraction(
                targetScope,
                leftParts[1],
                ForRestScriptExtractionSource.Body,
                string.Empty,
                regexRemainder[5..].Trim(),
                out extraction,
                out errorMessage);
        }

        if (regexRemainder.StartsWith("header ", StringComparison.OrdinalIgnoreCase))
        {
            var headerRemainder = regexRemainder[7..].Trim();
            if (!TryReadQuotedToken(headerRemainder, out var headerName, out var afterHeader))
            {
                errorMessage = "Regex header extractions must declare a quoted header name.";
                return false;
            }

            return TryParseRegexExtraction(
                targetScope,
                leftParts[1],
                ForRestScriptExtractionSource.Header,
                headerName,
                afterHeader.Trim(),
                out extraction,
                out errorMessage);
        }

        if (regexRemainder.StartsWith("json ", StringComparison.OrdinalIgnoreCase))
        {
            var jsonRemainder = regexRemainder[5..].Trim();
            if (!TryReadQuotedToken(jsonRemainder, out var selector, out var afterSelector))
            {
                errorMessage = "Regex JSON extractions must declare a quoted JSON selector.";
                return false;
            }

            return TryParseRegexExtraction(
                targetScope,
                leftParts[1],
                ForRestScriptExtractionSource.Json,
                selector,
                afterSelector.Trim(),
                out extraction,
                out errorMessage);
        }

        errorMessage = "Regex extractions must target 'body', 'header', or 'json'.";
        return false;
    }

    private static bool TryParseRegexExtraction(
        VariableScope targetScope,
        string targetVariableName,
        ForRestScriptExtractionSource source,
        string selector,
        string regexText,
        out ForRestScriptExtraction? extraction,
        out string? errorMessage)
    {
        extraction = null;
        errorMessage = null;

        if (!TryReadQuotedToken(regexText, out var pattern, out var afterPattern))
        {
            errorMessage = "Regex extractions must declare a quoted pattern.";
            return false;
        }

        if (!TryReadOptionalInteger(afterPattern.Trim(), out var group))
        {
            errorMessage = "Regex extractions may only use an optional capture group number.";
            return false;
        }

        extraction = new(targetScope, targetVariableName, source, selector, pattern!, group ?? 1);
        return true;
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

            string assertionInput = trimmed.StartsWith("expect ", StringComparison.OrdinalIgnoreCase)
                ? trimmed[7..].Trim()
                : trimmed;

            if (TryParseStatusAssertion(assertionInput, out var parsedAssertion)
                || TryParseBodyAssertion(assertionInput, out parsedAssertion)
                || TryParseHeaderAssertion(assertionInput, out parsedAssertion)
                || TryParseJsonAssertion(assertionInput, out parsedAssertion))
            {
                tests.Add(parsedAssertion!);
                index++;
                continue;
            }

            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            {
                var description = trimmed[1..^1];
                tests.Add(new(
                    ForRestScriptAssertionTarget.Status,
                    ForRestScriptComparisonOperator.Equal,
                    description,
                    IsDescriptionOnly: true));
                index++;
                continue;
            }

            diagnostics.Add(CreateDiagnostic("Could not parse the test assertion.", index + 1, line));
            index++;
        }

        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "The 'tests' section is missing a closing '}'.", index, 1));
    }

    private static bool TryParseFlow(
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        out string? flow)
    {
        flow = null;
        var line = lines[index];
        var trimmed = line.Trim();
        if (!string.Equals(trimmed, "flow {", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var builder = new StringBuilder();
        var depth = 1;
        index++;

        while (index < lines.Count)
        {
            var currentLine = lines[index];
            var currentTrimmed = currentLine.Trim();
            var braceDelta = CountBraceDelta(currentLine);

            if (depth == 1 && braceDelta == -1 && currentTrimmed == "}")
            {
                index++;
                flow = builder.ToString().TrimEnd();
                return true;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(currentLine);
            depth += braceDelta;
            index++;
        }

        diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "The 'flow' section is missing a closing '}'.", index, 1));
        flow = builder.ToString().TrimEnd();
        return true;
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

        var remainder = trimmedLine[5..].Trim();
        if (remainder.StartsWith("regex ", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRegexAssertion(
                ForRestScriptAssertionTarget.Body,
                string.Empty,
                remainder[6..].Trim(),
                out assertion);
        }

        if (!TryReadMessageAssertion(remainder, out var expressionText, out var message)
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

        if (afterHeader.TrimStart().StartsWith("regex ", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRegexAssertion(
                ForRestScriptAssertionTarget.Header,
                headerName,
                afterHeader.TrimStart()[6..].Trim(),
                out assertion);
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

        if (afterSelector.TrimStart().StartsWith("regex ", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRegexAssertion(
                ForRestScriptAssertionTarget.Json,
                string.Empty,
                afterSelector.TrimStart()[6..].Trim(),
                out assertion,
                selector);
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

    private static bool TryParseRegexAssertion(
        ForRestScriptAssertionTarget target,
        string headerName,
        string regexText,
        out ForRestScriptAssertion? assertion,
        string? selector = null)
    {
        assertion = null;
        if (!TryReadQuotedToken(regexText, out var pattern, out var afterPattern))
        {
            return false;
        }

        if (!TryReadQuotedToken(afterPattern.Trim(), out var message, out var remainder) || !string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        assertion = new(
            target,
            ForRestScriptComparisonOperator.RegexMatch,
            message!,
            HeaderName: string.IsNullOrWhiteSpace(headerName) ? null : headerName,
            Selector: selector,
            Value: new ForRestScriptStringExpression(pattern!));
        return true;
    }

    private static bool TryReadMessageAssertion(string text, out string expressionText, out string? message)
    {
        expressionText = string.Empty;
        message = null;
        text = TrimOptionalTerminator(text);
        if (!TryReadTrailingQuotedString(text, out expressionText, out message))
        {
            return false;
        }
        return true;
    }

    private static bool TryReadQuotedToken(string text, out string value, out string remainder)
    {
        return TryReadQuotedToken(text, out value, out remainder, out _);
    }

    private static bool TryReadQuotedToken(string text, out string value, out string remainder, out int consumedLength)
    {
        value = string.Empty;
        remainder = string.Empty;
        consumedLength = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.TrimStart();
        if (!IsSupportedQuotedStringStart(text[0]))
        {
            return false;
        }

        if (!TryFindQuotedTokenEnd(text, 0, out int endIndex))
        {
            return false;
        }

        var raw = text[..(endIndex + 1)];
        if (!TryParseQuotedString(raw, out var parsed))
        {
            return false;
        }

        value = parsed!;
        remainder = endIndex + 1 < text.Length ? text[(endIndex + 1)..] : string.Empty;
        consumedLength = endIndex + 1;
        return true;
    }

    private static bool TryReadOptionalInteger(string text, out int? value)
    {
        text = TrimOptionalTerminator(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (int.TryParse(text, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
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
        var trimmed = TrimOptionalTerminator(rawValue);
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

        if (Regex.IsMatch(trimmed, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            expression = new ForRestScriptIdentifierExpression(trimmed);
            return true;
        }

        return false;
    }

    private static string TrimOptionalTerminator(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.EndsWith(';')
            ? trimmed[..^1].TrimEnd()
            : trimmed;
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
        var stringDelimiter = '\0';

        foreach (var character in rawArguments)
        {
            if (inString)
            {
                if (IsMatchingQuotedStringDelimiter(stringDelimiter, character) && !escaped)
                {
                    inString = false;
                }
            }
            else if (IsSupportedQuotedStringStart(character))
            {
                inString = true;
                stringDelimiter = character;
            }
            else
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
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        string normalized = NormalizeQuotedStringLiteral(rawValue);
        if (!normalized.StartsWith('"') || !normalized.EndsWith('"'))
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<string>(normalized);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeQuotedStringLiteral(string rawValue)
    {
        if (string.IsNullOrEmpty(rawValue))
        {
            return string.Empty;
        }

        char[] characters = rawValue.ToCharArray();
        if (IsSupportedDoubleQuoteDelimiter(characters[0]))
        {
            characters[0] = '"';
        }

        int lastIndex = characters.Length - 1;
        if (lastIndex >= 0 && IsSupportedDoubleQuoteDelimiter(characters[lastIndex]))
        {
            characters[lastIndex] = '"';
        }

        return new string(characters);
    }

    private static bool IsSupportedQuotedStringStart(char character)
    {
        return IsSupportedDoubleQuoteDelimiter(character);
    }

    private static bool IsSupportedDoubleQuoteDelimiter(char character)
    {
        return character is '"' or '\u201C' or '\u201D' or '\u201E' or '\u00AB' or '\u00BB';
    }

    private static bool IsMatchingQuotedStringDelimiter(char openingDelimiter, char candidate)
    {
        return openingDelimiter switch
        {
            '"' or '\u201C' or '\u201D' or '\u201E' or '\u00AB' or '\u00BB' => IsSupportedDoubleQuoteDelimiter(candidate),
            _ => candidate == openingDelimiter,
        };
    }

    private static bool TryReadTrailingQuotedString(string text, out string expressionText, out string? value)
    {
        expressionText = string.Empty;
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        for (int index = 0; index < text.Length; index++)
        {
            if (!IsSupportedQuotedStringStart(text[index]))
            {
                continue;
            }

            if (!TryReadQuotedToken(text[index..], out var parsedValue, out var remainder, out var consumedLength))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(remainder))
            {
                index += Math.Max(consumedLength - 1, 0);
                continue;
            }

            expressionText = text[..index].Trim();
            if (string.IsNullOrWhiteSpace(expressionText))
            {
                return false;
            }

            value = parsedValue;
            return true;
        }

        return false;
    }

    private static bool TryFindQuotedTokenEnd(string text, int startIndex, out int endIndex)
    {
        endIndex = -1;
        if (string.IsNullOrWhiteSpace(text) ||
            startIndex < 0 ||
            startIndex >= text.Length ||
            !IsSupportedQuotedStringStart(text[startIndex]))
        {
            return false;
        }

        char openingDelimiter = text[startIndex];
        bool escaped = false;
        for (int index = startIndex + 1; index < text.Length; index++)
        {
            char character = text[index];
            if (IsMatchingQuotedStringDelimiter(openingDelimiter, character) && !escaped)
            {
                endIndex = index;
                return true;
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

        return false;
    }

    private static bool IsFlowSafeVariableName(string value)
    {
        return Regex.IsMatch(value ?? string.Empty, @"^[A-Za-z_][A-Za-z0-9_]*$");
    }

    private static int CountBraceDelta(string line)
    {
        var delta = 0;
        var inString = false;
        var escaped = false;
        var stringDelimiter = '\0';

        foreach (var character in line)
        {
            if (character == '#' && !inString)
            {
                break;
            }

            if (inString)
            {
                if (IsMatchingQuotedStringDelimiter(stringDelimiter, character) && !escaped)
                {
                    inString = false;
                }
            }
            else if (IsSupportedQuotedStringStart(character) || character == '\'')
            {
                inString = true;
                stringDelimiter = character;
            }
            else
            {
                if (character == '{')
                {
                    delta++;
                }
                else if (character == '}')
                {
                    delta--;
                }
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

        return delta;
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
            case "secret":
                scope = ForRestScriptVariableScope.Secret;
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
