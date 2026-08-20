using System.Text.RegularExpressions;

namespace ForRest.Scripting;

public sealed class ForRestScriptDocumentTextService
{
    private static readonly Regex NamePattern = new(
        "(?m)^[ \\t]*name(?:[ \\t]*=[ \\t]*|[ \\t]+)\"(?<name>(?:[^\"\\\\]|\\\\.)*)\"[ \\t]*$",
        RegexOptions.Compiled);

    private readonly ForRestScriptParser _parser = new();

    public ForRestScriptEditableSections Extract(string source)
    {
        string normalized = Normalize(source);
        ForRestScriptParseResult parseResult = _parser.Parse(normalized);
        if (parseResult.Document is not null)
        {
            return new()
            {
                Name = ReadName(parseResult.Document) ?? ExtractNameFallback(normalized),
                Variables = string.Join('\n', parseResult.Document.Variables.Select(RenderVariable)),
                Headers = string.Join('\n', parseResult.Document.Headers.Select(RenderHeader)),
                BodyMode = parseResult.Document.Body?.Mode ?? RequestBodyMode.Json,
                Body = parseResult.Document.Body?.Content ?? string.Empty,
                Tests = string.Join('\n', parseResult.Document.Tests.Select(RenderAssertionWithoutExpect)),
            };
        }

        return new()
        {
            Name = ExtractNameFallback(normalized),
        };
    }

    public string UpsertMetaName(string source, string name)
    {
        ForRestScriptDocument document = ParseOrCreate(source);
        document.Meta["name"] = new ForRestScriptStringExpression(name);
        return ForRestScriptDocumentRenderer.Render(document);
    }

    public string UpsertVariables(string source, string body)
    {
        ForRestScriptDocument document = ParseOrCreate(source);
        IReadOnlyList<ForRestScriptVariableDeclaration> variables = ParseOrCreate(Normalize(body)).Variables;
        return ForRestScriptDocumentRenderer.Render(document with
        {
            Variables = [.. variables]
        });
    }

    public string UpsertHeaders(string source, string body)
    {
        ForRestScriptDocument document = ParseOrCreate(source);
        IReadOnlyList<ForRestScriptNamedValue> headers = ParseOrCreate(EnsureDirectivePrefix(body, "header")).Headers;
        return ForRestScriptDocumentRenderer.Render(document with
        {
            Headers = [.. headers]
        });
    }

    public string UpsertTests(string source, string body)
    {
        ForRestScriptDocument document = ParseOrCreate(source);
        IReadOnlyList<ForRestScriptAssertion> tests = ParseOrCreate(EnsureDirectivePrefix(body, "expect")).Tests;
        return ForRestScriptDocumentRenderer.Render(document with
        {
            Tests = [.. tests]
        });
    }

    public string UpsertBody(string source, RequestBodyMode mode, string body)
    {
        ForRestScriptDocument document = ParseOrCreate(source);
        return ForRestScriptDocumentRenderer.Render(document with
        {
            Body = new(mode, Normalize(body).Trim('\n'))
        });
    }

    private ForRestScriptDocument ParseOrCreate(string source)
    {
        return _parser.Parse(source).Document ?? new();
    }

    private static string EnsureDirectivePrefix(string body, string directive)
    {
        List<string> lines = [];
        foreach (string rawLine in Normalize(body).Split('\n'))
        {
            string trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            lines.Add(trimmed.StartsWith(directive + " ", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"{directive} {trimmed}");
        }

        return string.Join('\n', lines);
    }

    private static string RenderVariable(ForRestScriptVariableDeclaration variable)
    {
        // Keep every scope keyword the parser accepts; collapsing 'secret' to 'runtime' would
        // silently strip masking and at-rest protection when the section is written back.
        string scope = variable.Scope switch
        {
            ForRestScriptVariableScope.Request => "request",
            ForRestScriptVariableScope.Secret => "secret",
            _ => "runtime",
        };
        return $"{scope} {variable.Key} = {RenderExpression(variable.Expression)}";
    }

    private static string RenderHeader(ForRestScriptNamedValue value)
    {
        return $"header \"{Escape(value.Key)}\" = {RenderExpression(value.Value)}";
    }

    private static string RenderAssertionWithoutExpect(ForRestScriptAssertion assertion)
    {
        return assertion.Target switch
        {
            ForRestScriptAssertionTarget.Status => $"status {RenderComparison(assertion)}",
            ForRestScriptAssertionTarget.Body => $"body {RenderComparison(assertion)}",
            ForRestScriptAssertionTarget.Header => $"header \"{Escape(assertion.HeaderName ?? string.Empty)}\" {RenderComparison(assertion)}",
            ForRestScriptAssertionTarget.Json => $"json \"{Escape(assertion.Selector ?? string.Empty)}\" {RenderComparison(assertion)}",
            _ => string.Empty
        };
    }

    private static string RenderComparison(ForRestScriptAssertion assertion)
    {
        if (assertion.Operator == ForRestScriptComparisonOperator.Exists)
        {
            return $"exists \"{Escape(assertion.Message)}\"";
        }

        if (assertion.Operator == ForRestScriptComparisonOperator.NotExists)
        {
            return $"not exists \"{Escape(assertion.Message)}\"";
        }

        return $"{RenderOperator(assertion.Operator)} {RenderExpression(assertion.Value ?? new ForRestScriptStringExpression(string.Empty))} \"{Escape(assertion.Message)}\"";
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
            ForRestScriptStringExpression stringExpression => $"\"{Escape(stringExpression.Value)}\"",
            ForRestScriptNumberExpression numberExpression => numberExpression.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ForRestScriptBooleanExpression booleanExpression => booleanExpression.Value ? "true" : "false",
            ForRestScriptIdentifierExpression identifierExpression => identifierExpression.Value,
            ForRestScriptFunctionCallExpression functionCallExpression => $"{functionCallExpression.Name}({string.Join(", ", functionCallExpression.Arguments.Select(RenderExpression))})",
            _ => expression.AsInvariantString()
        };
    }

    private static string? ReadName(ForRestScriptDocument document)
    {
        if (!document.Meta.TryGetValue("name", out ForRestScriptValueExpression? expression))
        {
            return null;
        }

        return expression is ForRestScriptStringExpression stringExpression
            ? stringExpression.Value
            : expression.AsInvariantString();
    }

    private static string ExtractNameFallback(string source)
    {
        Match match = NamePattern.Match(source);
        return match.Success ? Regex.Unescape(match.Groups["name"].Value) : string.Empty;
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string Normalize(string source)
    {
        return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }
}

public sealed record ForRestScriptEditableSections
{
    public string Name { get; init; } = string.Empty;

    public string Variables { get; init; } = string.Empty;

    public string Headers { get; init; } = string.Empty;

    public RequestBodyMode BodyMode { get; init; } = RequestBodyMode.Json;

    public string Body { get; init; } = string.Empty;

    public string Tests { get; init; } = string.Empty;
}
