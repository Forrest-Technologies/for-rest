using System.Text;

namespace ForRest.Scripting;

public static class ForRestScriptDocumentRenderer
{
    public static string Render(ForRestScriptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<string> lines = [];

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

        return string.Join('\n', TrimBlankLines(lines));
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
            string scope = variable.Scope == ForRestScriptVariableScope.Request ? "request" : "runtime";
            lines.Add($"{scope} {variable.Key} = {RenderExpression(variable.Expression)}");
        }
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
        lines.AddRange(Normalize(body.Content).Split('\n'));
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
            string scope = extraction.TargetScope == VariableScope.RequestLocal ? "request" : "runtime";
            lines.Add($"extract {scope} {extraction.TargetVariableName} = json {RenderQuotedString(extraction.Selector)}");
        }
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
