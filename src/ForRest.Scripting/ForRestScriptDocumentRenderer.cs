using System.Text;

namespace ForRest.Scripting;

public static class ForRestScriptDocumentRenderer
{
    public static string Render(ForRestScriptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<string> lines = [];

        AppendImports(lines, document.Imports);
        AppendMeta(lines, document.Meta);
        AppendRequest(lines, document.Request);
        AppendAuth(lines, document.Auth);
        AppendVariables(lines, document.Variables);
        AppendNamedValues(lines, "query", document.QueryParameters);
        AppendNamedValues(lines, "header", document.Headers);
        AppendBody(lines, document.Body);
        AppendNamedValues(lines, "form", document.FormValues);
        AppendNamedValues(lines, "multipart", document.MultipartValues);
        AppendExtractions(lines, document.Extractions);
        AppendFlow(lines, document.Flow);
        AppendTests(lines, document.Tests);
        AppendTopLevelKeyValues(lines, "repeat", document.Repeat);
        AppendTopLevelKeyValues(lines, "retry", document.Retry);
        AppendHandlers(lines, document.Handlers);
        AppendScenarios(lines, document.Scenarios);

        return string.Join('\n', TrimBlankLines(lines));
    }

    // Imports render first so shared definitions are declared before anything that uses them.
    // The parser accepts 'use' as an alias, but 'import' is the canonical spelling.
    private static void AppendImports(List<string> lines, IReadOnlyList<string> imports)
    {
        foreach (var import in imports)
        {
            // The parser reads the path between the quotes literally (no escape handling),
            // so the raw path is emitted as-is rather than escaped.
            lines.Add($"import \"{import}\"");
        }
    }

    private static void AppendMeta(List<string> lines, Dictionary<string, ForRestScriptValueExpression> meta)
    {
        if (meta.Count == 0)
        {
            return;
        }

        if (meta.TryGetValue("name", out ForRestScriptValueExpression? nameExpression))
        {
            AppendDirectiveLine(lines, "name", nameExpression);
        }

        Dictionary<string, ForRestScriptValueExpression> extraMeta = meta
            .Where(static item => !string.Equals(item.Key, "name", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase);

        if (extraMeta.Count > 0)
        {
            AppendAssignmentSection(lines, "meta", extraMeta);
        }
    }

    private static void AppendRequest(List<string> lines, Dictionary<string, ForRestScriptValueExpression> request)
    {
        if (request.Count == 0)
        {
            return;
        }

        string[] orderedKeys =
        [
            "method",
            "url",
            "timeout",
            "max_send_iterations",
            "redirects",
            "ssl",
            "history",
            "content_type"
        ];

        HashSet<string> renderedKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in orderedKeys)
        {
            if (!request.TryGetValue(key, out ForRestScriptValueExpression? expression))
            {
                continue;
            }

            AppendDirectiveLine(lines, key, expression);
            renderedKeys.Add(key);
        }

        Dictionary<string, ForRestScriptValueExpression> extraRequest = request
            .Where(item => !renderedKeys.Contains(item.Key))
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase);

        if (extraRequest.Count > 0)
        {
            AppendAssignmentSection(lines, "request", extraRequest);
        }
    }

    private static void AppendVariables(List<string> lines, IReadOnlyList<ForRestScriptVariableDeclaration> variables)
    {
        if (variables.Count == 0)
        {
            return;
        }

        EnsureSeparated(lines);
        foreach (ForRestScriptVariableDeclaration variable in variables)
        {
            lines.Add($"{RenderVariableScope(variable.Scope)} {variable.Key} = {RenderExpression(variable.Expression)}");
        }
    }

    // Rendering must round-trip every scope keyword the parser accepts; collapsing 'secret' to
    // 'runtime' would silently strip masking and at-rest protection on the next compile.
    private static string RenderVariableScope(ForRestScriptVariableScope scope)
    {
        return scope switch
        {
            ForRestScriptVariableScope.Request => "request",
            ForRestScriptVariableScope.Secret => "secret",
            _ => "runtime",
        };
    }

    private static void AppendNamedValues(List<string> lines, string keyword, IReadOnlyList<ForRestScriptNamedValue> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        EnsureSeparated(lines);
        foreach (ForRestScriptNamedValue item in values)
        {
            lines.Add($"{keyword} {RenderQuotedString(item.Key)} = {RenderExpression(item.Value)}");
        }
    }

    private static void AppendBody(List<string> lines, ForRestScriptBodySection? body)
    {
        if (body is null)
        {
            return;
        }

        EnsureSeparated(lines);
        string mode = body.Mode switch
        {
            RequestBodyMode.Json => "json",
            RequestBodyMode.RawText => "raw",
            RequestBodyMode.FormUrlEncoded => "form",
            RequestBodyMode.MultipartFormData => "multipart",
            _ => "raw",
        };

        lines.Add($"body {mode} \"\"\"");

        // The parser records the closing delimiter line as exactly one trailing empty content
        // entry, so exactly one trailing newline is removed here to keep parse -> render cycles
        // from growing the body by one blank line per round trip. Trimming every trailing
        // newline would destroy intentional blank lines at the end of a payload.
        var content = Normalize(body.Content);
        if (content.EndsWith('\n'))
        {
            content = content[..^1];
        }

        lines.AddRange(content.Split('\n'));
        lines.Add("\"\"\"");
    }

    private static void AppendExtractions(List<string> lines, IReadOnlyList<ForRestScriptExtraction> extractions)
    {
        if (extractions.Count == 0)
        {
            return;
        }

        EnsureSeparated(lines);
        foreach (ForRestScriptExtraction extraction in extractions)
        {
            lines.Add(RenderExtraction(extraction));
        }
    }

    // Regex extractions carry a pattern (and optional capture group) the parser distinguishes
    // from plain JSON selector extractions; collapsing them all to 'json' would silently delete
    // the pattern from user files on the next editor round trip.
    private static string RenderExtraction(ForRestScriptExtraction extraction)
    {
        var scope = extraction.TargetScope == VariableScope.RequestLocal ? "request" : "runtime";
        var prefix = $"extract {scope} {extraction.TargetVariableName} =";
        var groupSuffix = extraction.Group == 1 ? string.Empty : $" {extraction.Group}";

        return extraction.Source switch
        {
            ForRestScriptExtractionSource.Body => $"{prefix} regex body {RenderQuotedString(extraction.Pattern)}{groupSuffix}",
            ForRestScriptExtractionSource.Header => $"{prefix} regex header {RenderQuotedString(extraction.Selector)} {RenderQuotedString(extraction.Pattern)}{groupSuffix}",
            _ when !string.IsNullOrEmpty(extraction.Pattern) => $"{prefix} regex json {RenderQuotedString(extraction.Selector)} {RenderQuotedString(extraction.Pattern)}{groupSuffix}",
            _ => $"{prefix} json {RenderQuotedString(extraction.Selector)}",
        };
    }

    private static void AppendFlow(List<string> lines, string flow)
    {
        string normalized = Normalize(flow).Trim('\n');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        EnsureSeparated(lines);
        lines.AddRange(normalized.Split('\n'));
    }

    private static void AppendTests(List<string> lines, IReadOnlyList<ForRestScriptAssertion> tests)
    {
        if (tests.Count == 0)
        {
            return;
        }

        EnsureSeparated(lines);
        foreach (ForRestScriptAssertion test in tests)
        {
            lines.Add($"expect {RenderAssertion(test)}");
        }
    }

    private static void AppendTopLevelKeyValues(List<string> lines, string keyword, Dictionary<string, ForRestScriptValueExpression> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        EnsureSeparated(lines);
        foreach ((string key, ForRestScriptValueExpression value) in values)
        {
            lines.Add($"{keyword} {key} = {RenderExpression(value)}");
        }
    }

    private static void AppendHandlers(List<string> lines, IReadOnlyList<ForRestScriptHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            EnsureSeparated(lines);
            var opening = handler.Kind == ForRestScriptHandlerKind.OnStatus
                ? $"on status {handler.StatusCode} {{"
                : "on error {";

            lines.Add(opening);
            AppendVerbatimBlockBody(lines, handler.Body);
            lines.Add("}");
        }
    }

    private static void AppendScenarios(List<string> lines, IReadOnlyList<ForRestScriptScenario> scenarios)
    {
        foreach (var scenario in scenarios)
        {
            EnsureSeparated(lines);
            lines.Add($"scenario \"{scenario.Name}\" {{");

            foreach ((var key, var value) in scenario.Auth)
            {
                lines.Add($"  auth {key} = {RenderExpression(value)}");
            }

            foreach (var header in scenario.Headers)
            {
                lines.Add($"  header {RenderQuotedString(header.Key)} = {RenderExpression(header.Value)}");
            }

            foreach (var test in scenario.Tests)
            {
                lines.Add($"  expect {RenderAssertion(test)}");
            }

            AppendVerbatimBlockBody(lines, scenario.Flow);
            lines.Add("}");
        }
    }

    // Handler bodies and scenario flow are stored as raw trimmed text; the parser re-trims only
    // the outer edges on the next parse, so interior indentation must be emitted untouched —
    // adding canonical indentation here would grow into the stored text on every round trip.
    private static void AppendVerbatimBlockBody(List<string> lines, string body)
    {
        var normalized = Normalize(body).Trim('\n');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        lines.AddRange(normalized.Split('\n'));
    }

    private static void AppendAuth(List<string> lines, Dictionary<string, ForRestScriptValueExpression> auth)
    {
        if (auth.Count == 0)
        {
            return;
        }

        AppendAssignmentSection(lines, "auth", auth);
    }

    private static void AppendAssignmentSection(List<string> lines, string sectionName, Dictionary<string, ForRestScriptValueExpression> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        EnsureSeparated(lines);
        lines.Add($"{sectionName} {{");
        foreach ((string key, ForRestScriptValueExpression value) in values)
        {
            lines.Add($"  {key} = {RenderExpression(value)}");
        }

        lines.Add("}");
    }

    private static void AppendDirectiveLine(List<string> lines, string keyword, ForRestScriptValueExpression expression)
    {
        EnsureSeparated(lines, onlyWhenCurrentGroupIsEmpty: true);
        lines.Add($"{keyword} {RenderExpression(expression)}");
    }

    private static string RenderAssertion(ForRestScriptAssertion assertion)
    {
        string subject = assertion.Target switch
        {
            ForRestScriptAssertionTarget.Status => "status",
            ForRestScriptAssertionTarget.Body => "body",
            ForRestScriptAssertionTarget.Header => $"header {RenderQuotedString(assertion.HeaderName ?? string.Empty)}",
            ForRestScriptAssertionTarget.Json => $"json {RenderQuotedString(assertion.Selector ?? string.Empty)}",
            _ => "status"
        };

        string operation = assertion.Operator switch
        {
            ForRestScriptComparisonOperator.Exists => "exists",
            ForRestScriptComparisonOperator.NotExists => "not exists",
            ForRestScriptComparisonOperator.RegexMatch => $"regex {RenderExpression(assertion.Value ?? new ForRestScriptStringExpression(string.Empty))}",
            _ => $"{RenderOperator(assertion.Operator)} {RenderExpression(assertion.Value ?? new ForRestScriptStringExpression(string.Empty))}"
        };

        return $"{subject} {operation} {RenderQuotedString(assertion.Message)}";
    }

    private static string RenderOperator(ForRestScriptComparisonOperator comparisonOperator)
    {
        return comparisonOperator switch
        {
            ForRestScriptComparisonOperator.Equal => "==",
            ForRestScriptComparisonOperator.NotEqual => "!=",
            ForRestScriptComparisonOperator.Contains => "contains",
            ForRestScriptComparisonOperator.StartsWith => "startswith",
            ForRestScriptComparisonOperator.EndsWith => "endswith",
            ForRestScriptComparisonOperator.GreaterThan => ">",
            ForRestScriptComparisonOperator.GreaterThanOrEqual => ">=",
            ForRestScriptComparisonOperator.LessThan => "<",
            ForRestScriptComparisonOperator.LessThanOrEqual => "<=",
            _ => "=="
        };
    }

    private static string RenderExpression(ForRestScriptValueExpression expression)
    {
        return expression switch
        {
            ForRestScriptStringExpression stringExpression => RenderQuotedString(stringExpression.Value),
            ForRestScriptNumberExpression numberExpression => numberExpression.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ForRestScriptBooleanExpression booleanExpression => booleanExpression.Value ? "true" : "false",
            ForRestScriptIdentifierExpression identifierExpression => identifierExpression.Value,
            ForRestScriptFunctionCallExpression functionCallExpression => RenderFunctionCall(functionCallExpression),
            _ => expression.AsInvariantString()
        };
    }

    private static string RenderFunctionCall(ForRestScriptFunctionCallExpression expression)
    {
        StringBuilder builder = new();
        builder.Append(expression.Name);
        builder.Append('(');
        builder.Append(string.Join(", ", expression.Arguments.Select(RenderExpression)));
        builder.Append(')');
        return builder.ToString();
    }

    private static string RenderQuotedString(string value)
    {
        return $"\"{(value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static IReadOnlyList<string> TrimBlankLines(List<string> lines)
    {
        int start = 0;
        while (start < lines.Count && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        int end = lines.Count - 1;
        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
        {
            end--;
        }

        if (end < start)
        {
            return [];
        }

        return lines.Skip(start).Take(end - start + 1).ToArray();
    }

    private static void EnsureSeparated(List<string> lines, bool onlyWhenCurrentGroupIsEmpty = false)
    {
        if (lines.Count == 0)
        {
            return;
        }

        if (onlyWhenCurrentGroupIsEmpty)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.Add(string.Empty);
        }
    }
}
