using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ForRest.Scripting;

internal static class ForRestFlowScriptCompiler
{
    private static readonly HashSet<string> ReservedIdentifiers =
    [
        "await",
        "break",
        "browser",
        "call",
        "continue",
        "define",
        "delay",
        "__flow",
        "console",
        "convert",
        "Convert",
        "crypto",
        "DateTimeOffset",
        "dynamic",
        "else",
        "encoding",
        "Encoding",
        "Enumerable",
        "Environment",
        "extract",
        "false",
        "from",
        "fuzz",
        "Guid",
        "if",
        "import",
        "in",
        "int",
        "Math",
        "let",
        "long",
        "new",
        "null",
        "object",
        "on",
        "parallel",
        "payloads",
        "pipe",
        "regex",
        "Regex",
        "request",
        "response",
        "retry",
        "runtime",
        "scenario",
        "secret",
        "snapshot",
        "stash",
        "string",
        "StringComparison",
        "strings",
        "switch",
        "tests",
        "time",
        "true",
        "Uri",
        "use",
        "var",
        "variables",
        "while",
        "with",
        "workspace",
        "json",
        "random",
    ];

    private static readonly (string Source, string Target)[] PropertyAliases =
    [
        ("response.status", "response.Status"),
        ("response.body", "response.Body"),
        ("response.content_type", "response.ContentType"),
        ("response.headers", "response.Headers"),
        ("response.json()", "response.Json()"),
        ("request.url", "request.Url"),
        ("request.method", "request.Method"),
        ("request.body", "request.Body"),
        ("request.content_type", "request.ContentType"),
        ("request.headers", "request.Headers"),
        ("request.max_send_iterations", "request.MaxSendIterations"),
        ("request.remaining_send_iterations", "request.RemainingSendIterations"),
        ("workspace.name", "workspace.Name"),
        ("workspace.id", "workspace.Id"),
        ("request.user_agent", "request.UserAgent"),
        ("request.custom_user_agent", "request.CustomUserAgent"),
    ];

    public static string Compile(
        string flowSource,
        IEnumerable<string> knownVariableNames,
        IEnumerable<string>? templateBoundVariableNames,
        List<ForRestScriptDiagnostic> diagnostics,
        bool emitRuntimePreamble = true)
    {
        if (string.IsNullOrWhiteSpace(flowSource))
        {
            return string.Empty;
        }

        var knownIdentifiers = NormalizeIdentifiers(knownVariableNames);
        HashSet<string> templateBoundIdentifiers =
        [
            .. (templateBoundVariableNames ?? [])
                .Where(IsFlowIdentifier)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        var builder = new StringBuilder();

        // The runtime preamble declares __flow and lifts every known variable into a
        // dynamic local. It must be emitted exactly once per generated C# scope. Handler
        // fragments (on status / on error) are spliced into a scope that already carries
        // the preamble, so they opt out via emitRuntimePreamble: false to avoid
        // redeclaring __flow and the known variables (which would be a CS0128/CS0136).
        if (emitRuntimePreamble)
        {
            AppendRuntimePreamble(builder, knownIdentifiers);
        }

        var lines = Normalize(flowSource).Split('\n');
        var index = 0;
        var tempCounter = 0;
        CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(knownIdentifiers, StringComparer.OrdinalIgnoreCase), templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: false);
        return builder.ToString().Trim();
    }

    /// <summary>
    /// Builds the standalone runtime preamble (the <c>__flow</c> runtime plus a dynamic
    /// local for every known variable). Use this when a generated scope needs the
    /// preamble but the flow body itself is compiled with <c>emitRuntimePreamble: false</c>
    /// (for example, the catch block that hosts on-error handlers).
    /// </summary>
    public static string BuildRuntimePreamble(IEnumerable<string> knownVariableNames)
    {
        var builder = new StringBuilder();
        AppendRuntimePreamble(builder, NormalizeIdentifiers(knownVariableNames));
        return builder.ToString();
    }

    private static List<string> NormalizeIdentifiers(IEnumerable<string> identifiers)
    {
        return identifiers
            .Where(IsFlowIdentifier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AppendRuntimePreamble(StringBuilder builder, IReadOnlyList<string> knownIdentifiers)
    {
        builder.AppendLine("var __flow = new ForRest.Scripting.ForRestFlowRuntime(variables);");
        foreach (var identifier in knownIdentifiers)
        {
            builder.Append("dynamic ");
            builder.Append(identifier);
            builder.Append(" = __flow.V(");
            builder.Append(RenderString(identifier));
            builder.AppendLine(");");
        }
    }

    private static void CompileBlock(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
        IReadOnlySet<string> templateBoundIdentifiers,
        ref int tempCounter,
        bool allowBlockTerminator)
    {
        while (index < lines.Count)
        {
            var line = lines[index];
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                builder.AppendLine();
                index++;
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                builder.Append("//");
                builder.AppendLine(trimmed[1..]);
                index++;
                continue;
            }

            if (trimmed == "}")
            {
                if (!allowBlockTerminator)
                {
                    diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "Unexpected closing brace in flow section.", index + 1, 1));
                }

                index++;
                return;
            }

            if (allowBlockTerminator && TryRewriteInlineElseTransition(lines, index, trimmed))
            {
                return;
            }

            if (trimmed.StartsWith("else", StringComparison.Ordinal))
            {
                return;
            }

            if (TryCompileDefineStatement(builder, lines, ref index, diagnostics, locals, templateBoundIdentifiers, ref tempCounter))
            {
                continue;
            }

            if (TryCompileParallelStatement(builder, lines, ref index, diagnostics, locals, ref tempCounter))
            {
                continue;
            }

            if (TryCompilePipeStatement(builder, lines, ref index, diagnostics, locals, ref tempCounter))
            {
                continue;
            }

            if (TryCompileIfStatement(builder, lines, ref index, diagnostics, locals, templateBoundIdentifiers, ref tempCounter))
            {
                continue;
            }

            if (TryCompileWhileStatement(builder, lines, ref index, diagnostics, locals, templateBoundIdentifiers, ref tempCounter))
            {
                continue;
            }

            if (TryCompileRetryStatement(builder, lines, ref index, diagnostics, locals, templateBoundIdentifiers, ref tempCounter))
            {
                continue;
            }

            if (TryCompileForEachStatement(builder, lines, ref index, diagnostics, locals, templateBoundIdentifiers, ref tempCounter))
            {
                continue;
            }

            if (TryCompileSwitchStatement(builder, lines, ref index, diagnostics, locals, templateBoundIdentifiers, ref tempCounter))
            {
                continue;
            }

            TryCollectStatementText(lines, index, out string statementText, out int consumedLineCount);

            if (IsUnsupportedClassicForLoop(trimmed))
            {
                diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "Classic C-style for loops are not supported in .frs flow. Use 'foreach item in range(...) { }' or 'while ... { }'.", index + 1, 1));
                index++;
                continue;
            }

            if (TryCompileExtractLetStatement(builder, statementText, locals))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryCompileLetStatement(builder, statementText, locals, templateBoundIdentifiers))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryCompileRuntimeStatement(builder, statementText, locals, ref tempCounter))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryCompileSecretStatement(builder, statementText, locals, ref tempCounter))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryCompileCallStatement(builder, statementText, locals))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryCompileSnapshotStatement(builder, statementText, locals))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryCompileStashColumnsStatement(builder, statementText))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryCompileLogStatement(builder, statementText, "Log", locals))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryCompileLogStatement(builder, statementText, "Warn", locals))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryCompileLogStatement(builder, statementText, "Error", locals))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryCompileDelayStatement(builder, statementText, locals))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryReportMisplacedExpectation(statementText, index, diagnostics))
            {
                index += consumedLineCount;
                continue;
            }

            if (TryReportMalformedLegacyHelperCall(statementText, index, diagnostics))
            {
                index += consumedLineCount;
                continue;
            }

            if (statementText.EndsWith('{'))
            {
                builder.AppendLine(TranslateRawStatement(statementText, locals));
                index += consumedLineCount;
                CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: true);
                builder.AppendLine("}");
                continue;
            }

            builder.AppendLine(TranslateRawStatement(statementText, locals));
            index += consumedLineCount;
        }

        if (allowBlockTerminator)
        {
            diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "Flow block is missing a closing brace.", lines.Count, 1));
        }
    }

    private static bool TryRewriteInlineElseTransition(IReadOnlyList<string> lines, int index, string trimmed)
    {
        if (!trimmed.StartsWith('}') || trimmed.Length < 2)
        {
            return false;
        }

        var remainder = trimmed[1..].TrimStart();
        if (!remainder.StartsWith("else", StringComparison.Ordinal))
        {
            return false;
        }

        if (lines is string[] mutableLines)
        {
            mutableLines[index] = remainder;
            return true;
        }

        return false;
    }

    private static bool TryCompileIfStatement(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
        IReadOnlySet<string> templateBoundIdentifiers,
        ref int tempCounter)
    {
        if (!TryReadBlockHeader(lines, index, "if", out var condition, out var consumedLineCount))
        {
            return false;
        }

        builder.Append("if (");
        builder.Append(TranslateExpression(condition, locals));
        builder.AppendLine(") {");
        index += consumedLineCount;
        CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: true);
        builder.AppendLine("}");

        while (index < lines.Count)
        {
            var elseLine = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(elseLine))
            {
                builder.AppendLine();
                index++;
                continue;
            }

            if (elseLine.StartsWith('#'))
            {
                builder.Append("//");
                builder.AppendLine(elseLine[1..]);
                index++;
                continue;
            }

            if (TryReadBlockHeader(lines, index, "else if", out var elseIfCondition, out consumedLineCount))
            {
                builder.Append("else if (");
                builder.Append(TranslateExpression(elseIfCondition, locals));
                builder.AppendLine(") {");
                index += consumedLineCount;
                CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: true);
                builder.AppendLine("}");
                continue;
            }

            if (IsElseBlockHeader(lines, index, out consumedLineCount))
            {
                builder.AppendLine("else {");
                index += consumedLineCount;
                CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: true);
                builder.AppendLine("}");
            }

            break;
        }

        return true;
    }

    private static bool TryCompileWhileStatement(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
        IReadOnlySet<string> templateBoundIdentifiers,
        ref int tempCounter)
    {
        if (!TryReadBlockHeader(lines, index, "while", out var condition, out var consumedLineCount))
        {
            return false;
        }

        builder.Append("while (");
        builder.Append(TranslateExpression(condition, locals));
        builder.AppendLine(") {");
        index += consumedLineCount;
        CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: true);
        builder.AppendLine("}");
        return true;
    }

    private static bool TryCompileForEachStatement(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
        IReadOnlySet<string> templateBoundIdentifiers,
        ref int tempCounter)
    {
        if (!TryReadForEachHeader(lines, index, out var iteratorName, out var sourceExpression, out var consumedLineCount))
        {
            return false;
        }

        builder.Append("foreach (dynamic ");
        builder.Append(iteratorName);
        builder.Append(" in ");
        builder.Append(TranslateExpression(sourceExpression, locals));
        builder.AppendLine(") {");
        var nestedLocals = new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase)
        {
            iteratorName
        };
        index += consumedLineCount;
        CompileBlock(builder, lines, ref index, diagnostics, nestedLocals, templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: true);
        builder.AppendLine("}");
        return true;
    }

    private static bool TryCompileLetStatement(
        StringBuilder builder,
        string trimmed,
        HashSet<string> locals,
        IReadOnlySet<string> templateBoundIdentifiers)
    {
        if (!TryReadKeywordRemainder(trimmed, "let", out var remainder))
        {
            return false;
        }

        if (!TrySplitAssignment(remainder, out var name, out var expression))
        {
            return false;
        }

        builder.Append(locals.Contains(name) ? string.Empty : "dynamic ");
        builder.Append(name);
        builder.Append(" = ");
        builder.Append(TranslateExpression(expression, locals));
        builder.AppendLine(";");
        locals.Add(name);

        if (templateBoundIdentifiers.Contains(name))
        {
            builder.Append("__flow.Bind(");
            builder.Append(RenderString(name));
            builder.Append(", ");
            builder.Append(name);
            builder.AppendLine(");");
        }

        return true;
    }

    private static bool TryCompileRuntimeStatement(
        StringBuilder builder,
        string trimmed,
        HashSet<string> locals,
        ref int tempCounter)
    {
        if (!TryReadKeywordRemainder(trimmed, "runtime", out var remainder))
        {
            return false;
        }

        if (!TrySplitAssignment(remainder, out var name, out var expression))
        {
            return false;
        }

        tempCounter++;
        var tempName = $"__runtimeValue{tempCounter}";
        builder.Append("dynamic ");
        builder.Append(tempName);
        builder.Append(" = ");
        builder.Append(TranslateExpression(expression, locals));
        builder.AppendLine(";");
        builder.Append("variables.Set(");
        builder.Append(RenderString(name));
        builder.Append(", __flow.S(");
        builder.Append(tempName);
        builder.AppendLine("));");
        if (locals.Contains(name))
        {
            builder.Append(name);
            builder.Append(" = ");
            builder.Append(tempName);
            builder.AppendLine(";");
        }
        else
        {
            builder.Append("dynamic ");
            builder.Append(name);
            builder.Append(" = ");
            builder.Append(tempName);
            builder.AppendLine(";");
            locals.Add(name);
        }

        return true;
    }

    private static bool TryCompileSecretStatement(
        StringBuilder builder,
        string trimmed,
        HashSet<string> locals,
        ref int tempCounter)
    {
        if (!TryReadKeywordRemainder(trimmed, "secret", out var remainder))
        {
            return false;
        }

        if (!TrySplitAssignment(remainder, out var name, out var expression))
        {
            return false;
        }

        tempCounter++;
        var tempName = $"__secretValue{tempCounter}";
        builder.Append("dynamic ");
        builder.Append(tempName);
        builder.Append(" = ");
        builder.Append(TranslateExpression(expression, locals));
        builder.AppendLine(";");
        builder.Append("variables.Set(");
        builder.Append(RenderString(name));
        builder.Append(", __flow.S(");
        builder.Append(tempName);
        builder.Append("), ");
        builder.Append("VariableScope.Runtime, ");
        builder.AppendLine("isSecret: true);");
        if (locals.Contains(name))
        {
            builder.Append(name);
            builder.Append(" = ");
            builder.Append(tempName);
            builder.AppendLine(";");
        }
        else
        {
            builder.Append("dynamic ");
            builder.Append(name);
            builder.Append(" = ");
            builder.Append(tempName);
            builder.AppendLine(";");
            locals.Add(name);
        }

        return true;
    }

    private static bool TryCompileSwitchStatement(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
        IReadOnlySet<string> templateBoundIdentifiers,
        ref int tempCounter)
    {
        if (!TryReadBlockHeader(lines, index, "switch", out var expression, out var consumedLineCount))
        {
            return false;
        }

        tempCounter++;
        var switchVar = $"__switchValue{tempCounter}";
        builder.Append("dynamic ");
        builder.Append(switchVar);
        builder.Append(" = ");
        builder.Append(TranslateExpression(expression, locals));
        builder.AppendLine(";");

        index += consumedLineCount;
        var isFirstCase = true;

        while (index < lines.Count)
        {
            var caseLine = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(caseLine))
            {
                index++;
                continue;
            }

            if (caseLine.StartsWith('#'))
            {
                builder.Append("//");
                builder.AppendLine(caseLine[1..]);
                index++;
                continue;
            }

            if (caseLine == "}")
            {
                index++;
                break;
            }

            if (TryReadBlockHeader(lines, index, "case", out var caseValue, out var caseConsumed))
            {
                builder.Append(isFirstCase ? "if (" : "else if (");
                builder.Append(switchVar);
                builder.Append(" == ");
                builder.Append(TranslateExpression(caseValue, locals));
                builder.AppendLine(") {");
                index += caseConsumed;
                CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: true);
                builder.AppendLine("}");
                isFirstCase = false;
                continue;
            }

            if (IsDefaultBlockHeader(lines, index, out var defaultConsumed))
            {
                builder.AppendLine(isFirstCase ? "{" : "else {");
                index += defaultConsumed;
                CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: true);
                builder.AppendLine("}");
                isFirstCase = false;
                continue;
            }

            diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "Expected 'case', 'default', or '}' inside switch block.", index + 1, 1));
            index++;
        }

        return true;
    }

    private static bool IsDefaultBlockHeader(IReadOnlyList<string> lines, int startIndex, out int consumedLineCount)
    {
        consumedLineCount = 0;
        if (!TryCollectHeaderText(lines, startIndex, out string combinedHeader, out consumedLineCount))
        {
            return false;
        }

        var withoutBrace = combinedHeader[..^1].TrimEnd();
        return string.Equals(withoutBrace, "default", StringComparison.Ordinal);
    }

    private static bool TryCompileLogStatement(
        StringBuilder builder,
        string trimmed,
        string level,
        HashSet<string> locals)
    {
        if (!TryReadKeywordRemainder(trimmed, level.ToLowerInvariant(), out var expression, allowOpenParenStart: true))
        {
            return false;
        }

        builder.Append("console.");
        builder.Append(level);
        builder.Append('(');
        builder.Append(TranslateExpression(expression, locals));
        builder.AppendLine(");");
        return true;
    }

    private static bool TryCompileDelayStatement(
        StringBuilder builder,
        string trimmed,
        HashSet<string> locals)
    {
        if (!TryReadKeywordRemainder(trimmed, "delay", out var expression, allowOpenParenStart: true))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expression))
        {
            builder.AppendLine("/* delay: missing expression */");
            return true;
        }

        // Generates: await Task.Delay((int)Math.Max(0, (long)(<expression>)));
        // Clamp to non-negative and cast through long so the DSL accepts double,
        // long, and int expressions consistently without overflow surprises.
        builder.Append("await System.Threading.Tasks.Task.Delay((int)System.Math.Max(0L, (long)(");
        builder.Append(TranslateExpression(expression, locals));
        builder.AppendLine(")));");
        return true;
    }

    private static bool TryCompileExtractLetStatement(
        StringBuilder builder,
        string trimmed,
        HashSet<string> locals)
    {
        if (!TryReadKeywordRemainder(trimmed, "let", out var remainder))
        {
            return false;
        }

        if (!TrySplitAssignment(remainder, out var name, out var extractExpression))
        {
            return false;
        }

        if (!TryReadKeywordRemainder(extractExpression, "extract", out var extractBody))
        {
            return false;
        }

        var fromIndex = extractBody.LastIndexOf(" from ", StringComparison.Ordinal);
        if (fromIndex < 0)
        {
            return false;
        }

        var typeAndSelector = extractBody[..fromIndex].Trim();
        var source = extractBody[(fromIndex + 6)..].Trim();
        var translatedSource = TranslateExpression(source, locals);

        if (TryReadKeywordRemainder(typeAndSelector, "json", out var jsonSelector))
        {
            var selectorValue = StripQuotes(jsonSelector);
            builder.Append("dynamic ");
            builder.Append(name);
            builder.Append(" = json.Select(");
            builder.Append(translatedSource);
            builder.Append(".Json(), ");
            builder.Append(RenderString(selectorValue));
            builder.AppendLine(");");
        }
        else if (TryReadKeywordRemainder(typeAndSelector, "header", out var headerName))
        {
            var headerValue = StripQuotes(headerName);
            builder.Append("dynamic ");
            builder.Append(name);
            builder.Append(" = ");
            builder.Append(translatedSource);
            builder.Append(".Headers[");
            builder.Append(RenderString(headerValue));
            builder.AppendLine("];");
        }
        else if (TryReadKeywordRemainder(typeAndSelector, "regex", out var regexPattern))
        {
            var patternValue = StripQuotes(regexPattern);
            builder.Append("dynamic ");
            builder.Append(name);
            builder.Append(" = regex.Match(");
            builder.Append(translatedSource);
            builder.Append(", ");
            builder.Append(RenderString(patternValue));
            builder.AppendLine(");");
        }
        else
        {
            return false;
        }

        locals.Add(name);
        return true;
    }

    private static bool TryCompileRetryStatement(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
        IReadOnlySet<string> templateBoundIdentifiers,
        ref int tempCounter)
    {
        if (!TryReadBlockHeader(lines, index, "retry", out var expression, out var consumedLineCount))
        {
            return false;
        }

        tempCounter++;
        var retryVar = $"__retryIdx{tempCounter}";

        var parts = expression.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out var maxRetries))
        {
            diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "retry requires a numeric count, e.g. 'retry 3 { }'.", index + 1, 1));
            index += consumedLineCount;
            return true;
        }

        builder.Append("for (int ");
        builder.Append(retryVar);
        builder.Append(" = 0; ");
        builder.Append(retryVar);
        builder.Append(" < ");
        builder.Append(maxRetries);
        builder.Append("; ");
        builder.Append(retryVar);
        builder.AppendLine("++) {");

        if (parts.Length >= 3 && string.Equals(parts[1], "with", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(parts[2], "backoff", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append("if (");
                builder.Append(retryVar);
                builder.Append(" > 0) { await Task.Delay((int)(100 * Math.Pow(2, ");
                builder.Append(retryVar);
                builder.AppendLine(" - 1))); }");
            }
            else if (string.Equals(parts[2], "delay", StringComparison.OrdinalIgnoreCase)
                     && parts.Length >= 4
                     && int.TryParse(parts[3], out var delayMs))
            {
                builder.Append("if (");
                builder.Append(retryVar);
                builder.Append(" > 0) { await Task.Delay(");
                builder.Append(delayMs);
                builder.AppendLine("); }");
            }
        }

        index += consumedLineCount;
        CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: true);
        builder.AppendLine("}");
        return true;
    }

    private static bool TryCompileStashColumnsStatement(
        StringBuilder builder,
        string trimmed)
    {
        if (!TryReadKeywordRemainder(trimmed, "stash", out var remainder))
        {
            return false;
        }

        if (!TryReadKeywordRemainder(remainder, "columns", out var columnList))
        {
            return false;
        }

        columnList = columnList.Trim();
        if (!columnList.StartsWith('[') || !columnList.EndsWith(']'))
        {
            return false;
        }

        var inner = columnList[1..^1].Trim();
        if (string.IsNullOrWhiteSpace(inner))
        {
            return false;
        }

        var columnNames = new List<string>();
        foreach (var part in SplitTopLevelCommas(inner))
        {
            var col = StripQuotes(part.Trim());
            if (!string.IsNullOrWhiteSpace(col))
            {
                columnNames.Add(col);
            }
        }

        if (columnNames.Count == 0)
        {
            return false;
        }

        builder.Append("stash.DeclareColumns(");
        builder.Append(string.Join(", ", columnNames.Select(static column => JsonSerializer.Serialize(column))));
        builder.AppendLine(");");
        return true;
    }

    private static bool TryCompileDefineStatement(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
        IReadOnlySet<string> templateBoundIdentifiers,
        ref int tempCounter)
    {
        if (!TryCollectHeaderText(lines, index, out string combinedHeader, out int consumedLineCount))
        {
            return false;
        }

        var withoutBrace = combinedHeader[..^1].TrimEnd();
        if (!withoutBrace.StartsWith("define", StringComparison.Ordinal))
        {
            return false;
        }

        var afterDefine = withoutBrace["define".Length..];
        if (afterDefine.Length == 0 || !char.IsWhiteSpace(afterDefine[0]))
        {
            return false;
        }

        afterDefine = afterDefine.Trim();

        string defineName;
        var parameterNames = new List<string>();

        var withIndex = afterDefine.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
        if (withIndex >= 0)
        {
            defineName = afterDefine[..withIndex].Trim();
            var paramList = afterDefine[(withIndex + 6)..].Trim();
            foreach (var param in paramList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IsFlowIdentifier(param))
                {
                    parameterNames.Add(param);
                }
            }
        }
        else
        {
            defineName = afterDefine.Trim();
        }

        if (!IsFlowIdentifier(defineName))
        {
            return false;
        }

        builder.Append("async Task __define_");
        builder.Append(defineName);
        builder.Append('(');
        builder.Append(string.Join(", ", parameterNames.Select(static parameter => $"dynamic {parameter}")));
        builder.AppendLine(") {");

        var nestedLocals = new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase);
        foreach (var param in parameterNames)
        {
            nestedLocals.Add(param);
        }

        index += consumedLineCount;
        CompileBlock(builder, lines, ref index, diagnostics, nestedLocals, templateBoundIdentifiers, ref tempCounter, allowBlockTerminator: true);
        builder.AppendLine("}");
        return true;
    }

    private static bool TryCompileCallStatement(
        StringBuilder builder,
        string trimmed,
        HashSet<string> locals)
    {
        if (!TryReadKeywordRemainder(trimmed, "call", out var remainder))
        {
            return false;
        }

        string callName;
        var arguments = new List<string>();

        var withIndex = remainder.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
        if (withIndex >= 0)
        {
            callName = remainder[..withIndex].Trim();
            var argList = remainder[(withIndex + 6)..].Trim();
            foreach (var arg in SplitTopLevelCommas(argList))
            {
                arguments.Add(TranslateExpression(arg.Trim(), locals));
            }
        }
        else
        {
            callName = TrimStatement(remainder);
        }

        if (!IsFlowIdentifier(callName))
        {
            return false;
        }

        builder.Append("await __define_");
        builder.Append(callName);
        builder.Append('(');
        builder.Append(string.Join(", ", arguments));
        builder.AppendLine(");");
        return true;
    }

    private static bool TryCompileParallelStatement(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
        ref int tempCounter)
    {
        if (!TryCollectHeaderText(lines, index, out string combinedHeader, out int consumedLineCount))
        {
            return false;
        }

        var withoutBrace = combinedHeader[..^1].TrimEnd();
        var destructuredNames = new List<string>();

        if (withoutBrace.StartsWith("let", StringComparison.Ordinal))
        {
            var afterLet = withoutBrace["let".Length..].Trim();
            if (!afterLet.StartsWith('['))
            {
                return false;
            }

            var bracketEnd = afterLet.IndexOf(']');
            if (bracketEnd < 0)
            {
                return false;
            }

            var nameList = afterLet[1..bracketEnd];
            foreach (var name in nameList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IsFlowIdentifier(name))
                {
                    destructuredNames.Add(name);
                }
            }

            var afterBracket = afterLet[(bracketEnd + 1)..].Trim();
            if (!afterBracket.StartsWith('='))
            {
                return false;
            }

            afterBracket = afterBracket[1..].Trim();
            if (!string.Equals(afterBracket, "parallel", StringComparison.Ordinal))
            {
                return false;
            }
        }
        else if (!string.Equals(withoutBrace, "parallel", StringComparison.Ordinal))
        {
            return false;
        }

        index += consumedLineCount;
        tempCounter++;
        var batchId = tempCounter;
        var taskNames = new List<string>();
        var requestIdx = 0;

        while (index < lines.Count)
        {
            var line = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                index++;
                continue;
            }

            if (line == "}")
            {
                index++;
                break;
            }

            requestIdx++;
            var cloneVar = $"__par{batchId}_{requestIdx}";
            var taskVar = $"__parTask{batchId}_{requestIdx}";
            taskNames.Add(taskVar);

            var parts = line.Split([' '], 2, StringSplitOptions.RemoveEmptyEntries);
            var method = parts[0].ToUpperInvariant();
            var url = parts.Length > 1 ? StripQuotes(parts[1].Trim()) : string.Empty;

            builder.Append("var ");
            builder.Append(cloneVar);
            builder.AppendLine(" = request.Clone();");
            builder.Append(cloneVar);
            builder.Append(".Method = ");
            builder.Append(RenderString(method));
            builder.AppendLine(";");

            if (!string.IsNullOrWhiteSpace(url))
            {
                builder.Append(cloneVar);
                builder.Append(".Url = ");
                builder.Append(TranslateExpression(RenderString(url), locals));
                builder.AppendLine(";");
            }

            builder.Append("var ");
            builder.Append(taskVar);
            builder.Append(" = ");
            builder.Append(cloneVar);
            builder.AppendLine(".SendAsync();");

            index++;
        }

        if (taskNames.Count > 0)
        {
            builder.Append("await Task.WhenAll(");
            builder.Append(string.Join(", ", taskNames));
            builder.AppendLine(");");

            for (int i = 0; i < destructuredNames.Count && i < taskNames.Count; i++)
            {
                builder.Append("dynamic ");
                builder.Append(destructuredNames[i]);
                builder.Append(" = ");
                builder.Append(taskNames[i]);
                builder.AppendLine(".Result;");
                locals.Add(destructuredNames[i]);
            }
        }

        return true;
    }

    private static bool TryCompilePipeStatement(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
        ref int tempCounter)
    {
        if (!TryCollectHeaderText(lines, index, out string combinedHeader, out int consumedLineCount))
        {
            return false;
        }

        var withoutBrace = combinedHeader[..^1].TrimEnd();
        if (!string.Equals(withoutBrace, "pipe", StringComparison.Ordinal))
        {
            return false;
        }

        index += consumedLineCount;

        while (index < lines.Count)
        {
            var line = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                index++;
                continue;
            }

            if (line == "}")
            {
                index++;
                break;
            }

            string? captureName = null;
            var arrowIndex = line.LastIndexOf("->", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                var afterArrow = line[(arrowIndex + 2)..].Trim();
                if (afterArrow.StartsWith("let ", StringComparison.Ordinal))
                {
                    captureName = afterArrow[4..].Trim();
                }

                line = line[..arrowIndex].Trim();
            }

            var parts = line.Split([' '], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                index++;
                continue;
            }

            var method = parts[0].ToUpperInvariant();
            var rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            builder.Append("request.Method = ");
            builder.Append(RenderString(method));
            builder.AppendLine(";");

            string? bodyContent = null;
            string? bodyContentType = null;
            var bodyJsonIdx = rest.IndexOf("body json", StringComparison.OrdinalIgnoreCase);
            if (bodyJsonIdx >= 0)
            {
                var urlPart = rest[..bodyJsonIdx].Trim();
                var bodyPart = rest[(bodyJsonIdx + 9)..].Trim();
                rest = urlPart;
                bodyContent = StripQuotes(bodyPart);
                bodyContentType = "application/json";
            }

            if (!string.IsNullOrWhiteSpace(rest))
            {
                var url = StripQuotes(rest);
                builder.Append("request.Url = ");
                builder.Append(TranslateExpression(RenderString(url), locals));
                builder.AppendLine(";");
            }

            if (bodyContent is not null)
            {
                builder.Append("request.ContentType = ");
                builder.Append(RenderString(bodyContentType!));
                builder.AppendLine(";");
                builder.Append("request.Body = ");
                builder.Append(TranslateExpression(RenderString(bodyContent), locals));
                builder.AppendLine(";");
            }

            if (captureName is not null && IsFlowIdentifier(captureName))
            {
                builder.Append("dynamic ");
                builder.Append(captureName);
                builder.AppendLine(" = (await request.SendAsync());");
                locals.Add(captureName);
            }
            else
            {
                builder.AppendLine("await request.SendAsync();");
            }

            index++;
        }

        return true;
    }

    private static bool TryCompileSnapshotStatement(
        StringBuilder builder,
        string trimmed,
        HashSet<string> locals)
    {
        if (!TryReadKeywordRemainder(trimmed, "snapshot", out var remainder))
        {
            return false;
        }

        var fromIndex = remainder.IndexOf(" from ", StringComparison.Ordinal);
        if (fromIndex < 0)
        {
            return false;
        }

        var snapshotName = StripQuotes(remainder[..fromIndex].Trim());
        var source = remainder[(fromIndex + 6)..].Trim();

        if (string.IsNullOrWhiteSpace(snapshotName) || string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        builder.Append("await snapshot.Save(");
        builder.Append(RenderString(snapshotName));
        builder.Append(", ");
        builder.Append(TranslateExpression(source, locals));
        builder.AppendLine(");");
        return true;
    }

    private static string StripQuotes(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2
            && ((trimmed[0] == '"' && trimmed[^1] == '"')
                || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static IReadOnlyList<string> SplitTopLevelCommas(string source)
    {
        var results = new List<string>();
        var depth = 0;
        var inString = false;
        var escaped = false;
        var start = 0;

        for (int i = 0; i < source.Length; i++)
        {
            var character = source[i];
            if (inString)
            {
                if (character == '\\' && !escaped) { escaped = true; continue; }
                if ((character == '"' || character == '\'') && !escaped) { inString = false; }
                escaped = false;
                continue;
            }

            switch (character)
            {
                case '"' or '\'':
                    inString = true;
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    results.Add(source[start..i]);
                    start = i + 1;
                    break;
            }
        }

        if (start < source.Length)
        {
            results.Add(source[start..]);
        }

        return results;
    }

    private static string TranslateRawStatement(string statement, HashSet<string> locals)
    {
        var trimmed = TrimStatement(statement);
        if (TryTranslateStructuredRequestBodyAssignment(trimmed, locals, out string structuredLiteralAssignment))
        {
            return structuredLiteralAssignment;
        }

        if (TryTranslateBareRequestDirectiveStatement(trimmed, locals, out string requestDirectiveAssignment))
        {
            return requestDirectiveAssignment;
        }

        var suffix = statement.TrimEnd().EndsWith('{') ? " {" : ";";
        if (suffix == " {")
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        return TranslateExpression(trimmed, locals) + suffix;
    }

    private static bool TryTranslateStructuredRequestBodyAssignment(
        string statement,
        HashSet<string> locals,
        out string translated)
    {
        translated = string.Empty;
        if (!TryFindAssignmentIndex(statement, out int separatorIndex) || separatorIndex < 1)
        {
            return false;
        }

        string left = statement[..separatorIndex].Trim();
        if (!string.Equals(left, "request.body", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string expression = TrimStatement(statement[(separatorIndex + 1)..]);
        if (!TryRenderStructuredJsonLiteral(expression, out string renderedJson))
        {
            if (!TryRenderDynamicStructuredJsonLiteral(expression, locals, out string renderedStructuredJsonExpression))
            {
                return false;
            }

            translated = $"{TranslateExpression(left, locals)} = json.Stringify({renderedStructuredJsonExpression}, false);";
            return true;
        }

        translated = $"{TranslateExpression(left, locals)} = {renderedJson};";
        return true;
    }

    private static bool TryTranslateBareRequestDirectiveStatement(
        string statement,
        HashSet<string> locals,
        out string translated)
    {
        translated = string.Empty;

        foreach ((string Keyword, string TargetProperty) mapping in new[]
                 {
                     ("method", "request.Method"),
                     ("url", "request.Url"),
                     ("content_type", "request.ContentType"),
                 })
        {
            if (!TryReadKeywordRemainder(statement, mapping.Keyword, out string remainder))
            {
                continue;
            }

            translated = $"{mapping.TargetProperty} = {RenderDirectiveMutationValue(mapping.Keyword, remainder, locals)};";
            return true;
        }

        if (!TryReadKeywordRemainder(statement, "header", out string headerRemainder))
        {
            return false;
        }

        if (!TryParseNamedDirectiveAssignment(headerRemainder, out string key, out string valueExpression))
        {
            return false;
        }

        translated = $"request.Headers[{RenderString(key)}] = {TranslateExpression(valueExpression, locals)};";
        return true;
    }

    private static string RenderDirectiveMutationValue(string keyword, string expression, HashSet<string> locals)
    {
        string trimmed = TrimStatement(expression);
        if (string.Equals(keyword, "method", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(trimmed, "^[A-Za-z]+$", RegexOptions.CultureInvariant))
        {
            return RenderString(trimmed.ToUpperInvariant());
        }

        return TranslateExpression(trimmed, locals);
    }

    private static bool TryParseNamedDirectiveAssignment(string remainder, out string key, out string valueExpression)
    {
        key = string.Empty;
        valueExpression = string.Empty;

        int index = 0;
        SkipWhitespace(remainder, ref index);
        if (!TryReadQuotedToken(remainder, ref index, out key))
        {
            return false;
        }

        SkipWhitespace(remainder, ref index);
        if (index >= remainder.Length || remainder[index] != '=')
        {
            return false;
        }

        index++;
        SkipWhitespace(remainder, ref index);
        if (index >= remainder.Length)
        {
            return false;
        }

        valueExpression = TrimStatement(remainder[index..]);
        return !string.IsNullOrWhiteSpace(valueExpression);
    }

    private static bool TryRenderStructuredJsonLiteral(string expression, out string renderedJson)
    {
        renderedJson = string.Empty;
        string normalized = Normalize(expression).Trim();
        if (!IsStructuredLiteralStart(normalized))
        {
            return false;
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(normalized);
        }
        catch (JsonException)
        {
            return false;
        }

        renderedJson = RenderString(normalized);
        return true;
    }

    private static bool TryRenderDynamicStructuredJsonLiteral(
        string expression,
        HashSet<string> locals,
        out string renderedExpression)
    {
        renderedExpression = string.Empty;
        string normalized = Normalize(expression).Trim();
        if (!IsStructuredLiteralStart(normalized))
        {
            return false;
        }

        if (!TryParseStructuredLiteralNode(normalized, out StructuredLiteralNode? node) ||
            node is null)
        {
            return false;
        }

        renderedExpression = RenderStructuredLiteralNode(node, locals);
        return true;
    }

    private static string TranslateExpression(string expression, HashSet<string> locals)
    {
        var translated = TrimStatement(expression);
        translated = RewriteOutsideQuotedText(
            translated,
            static segment =>
            {
                foreach (var (source, target) in PropertyAliases)
                {
                    segment = segment.Replace(source, target, StringComparison.OrdinalIgnoreCase);
                }

                segment = segment.Replace("guid()", "random.Guid()", StringComparison.OrdinalIgnoreCase);
                segment = segment.Replace("now()", "time.Now", StringComparison.OrdinalIgnoreCase);
                segment = segment.Replace("utc_now()", "time.UtcNow", StringComparison.OrdinalIgnoreCase);
                segment = RewriteKeywordBooleanOperators(segment);
                segment = RewriteRangeLiterals(segment);
                segment = RewriteCountAliases(segment);
                segment = segment.Replace("range(", "__flow.Range(", StringComparison.OrdinalIgnoreCase);
                segment = segment.Replace("random(", "random.Number(", StringComparison.OrdinalIgnoreCase);
                return segment;
            });
        translated = NormalizeSendCalls(translated);

        var builder = new StringBuilder();
        for (var index = 0; index < translated.Length; index++)
        {
            var character = translated[index];
            if (character == '$' && index + 1 < translated.Length && IsSupportedDoubleQuoteDelimiter(translated[index + 1]))
            {
                AppendInterpolatedString(builder, translated, ref index, locals);
                continue;
            }

            if (IsSupportedDoubleQuoteDelimiter(character))
            {
                AppendNormalizedDoubleQuotedString(builder, translated, ref index);
                continue;
            }

            if (character == '\'')
            {
                AppendSingleQuotedString(builder, translated, ref index);
                continue;
            }

            if (IsIdentifierStart(character))
            {
                var start = index;
                index++;
                while (index < translated.Length && IsIdentifierPart(translated[index]))
                {
                    index++;
                }

                var token = translated[start..index];
                index--;
                var previous = start > 0 ? translated[start - 1] : '\0';
                if (char.IsUpper(token[0]) ||
                    previous == '.' ||
                    previous == '@' ||
                    locals.Contains(token) ||
                    ReservedIdentifiers.Contains(token))
                {
                    builder.Append(token);
                    continue;
                }

                builder.Append("__flow.V(");
                builder.Append(RenderString(token));
                builder.Append(')');
                continue;
            }

            builder.Append(character);
        }

        return RewriteCollectionMethodChains(builder.ToString());
    }

    private static readonly HashSet<string> CollectionMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "where", "select", "first", "firstOrDefault", "last", "any", "all",
        "count", "orderBy", "orderByDesc", "take", "skip", "distinct",
        "flatten", "groupBy", "sum", "min", "max", "average", "toList",
        "reverse", "contains",
    };

    private static readonly Dictionary<string, string> CollectionMethodNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["where"] = "Where",
        ["select"] = "Select",
        ["first"] = "First",
        ["firstOrDefault"] = "FirstOrDefault",
        ["firstordefault"] = "FirstOrDefault",
        ["last"] = "Last",
        ["any"] = "Any",
        ["all"] = "All",
        ["count"] = "Count",
        ["orderBy"] = "OrderBy",
        ["orderby"] = "OrderBy",
        ["orderByDesc"] = "OrderByDesc",
        ["orderbydesc"] = "OrderByDesc",
        ["take"] = "Take",
        ["skip"] = "Skip",
        ["distinct"] = "Distinct",
        ["flatten"] = "Flatten",
        ["groupBy"] = "GroupBy",
        ["groupby"] = "GroupBy",
        ["sum"] = "Sum",
        ["min"] = "Min",
        ["max"] = "Max",
        ["average"] = "Average",
        ["toList"] = "ToList",
        ["tolist"] = "ToList",
        ["reverse"] = "Reverse",
        ["contains"] = "Contains",
    };

    private static readonly HashSet<string> BuiltInApiNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "request", "response", "variables", "tests", "console",
        "time", "strings", "convert", "json", "encoding", "crypto",
        "regex", "random", "workspace", "stash", "snapshot",
        "CollectionApi", "Math", "Task", "string", "int", "double",
        "__flow", "__onErrorEx",
    };

    private static string RewriteCollectionMethodChains(string expression)
    {
        var result = expression;
        bool rewritten;
        do
        {
            rewritten = false;
            foreach (var method in CollectionMethods)
            {
                var pattern = $".{method}(";
                var idx = FindCollectionMethodCall(result, pattern);
                if (idx < 0)
                {
                    continue;
                }

                var receiver = ExtractReceiver(result, idx);
                if (string.IsNullOrWhiteSpace(receiver))
                {
                    continue;
                }

                // Don't rewrite method calls on known built-in API objects
                if (BuiltInApiNames.Contains(receiver))
                {
                    continue;
                }

                var argsStart = idx + pattern.Length;
                var argsEnd = FindMatchingCloseParen(result, argsStart - 1);
                if (argsEnd < 0)
                {
                    continue;
                }

                var args = result[argsStart..argsEnd].Trim();
                var methodName = CollectionMethodNameMap.GetValueOrDefault(method, method);

                string replacement;
                if (string.IsNullOrWhiteSpace(args))
                {
                    replacement = $"CollectionApi.{methodName}({receiver})";
                }
                else if (args.Contains("=>"))
                {
                    replacement = $"CollectionApi.{methodName}({receiver}, (Func<object?,object?>)({args}))";
                }
                else
                {
                    replacement = $"CollectionApi.{methodName}({receiver}, {args})";
                }

                var receiverStart = idx - receiver.Length;
                result = string.Concat(result.AsSpan(0, receiverStart), replacement, result.AsSpan(argsEnd + 1));
                rewritten = true;
                break;
            }
        } while (rewritten);

        return result;
    }

    private static int FindCollectionMethodCall(string text, string pattern)
    {
        const string alreadyRewritten = "CollectionApi";
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '"' || text[index] == '\'')
            {
                SkipQuotedString(text, ref index);
                continue;
            }

            if (index + pattern.Length <= text.Length &&
                text.AsSpan(index, pattern.Length).Equals(pattern.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                // Skip matches that are already rewritten (e.g., CollectionApi.Contains(...))
                // Pattern starts with '.', so index points at the dot. Check if "CollectionApi" precedes it.
                if (index >= alreadyRewritten.Length &&
                    text.AsSpan(index - alreadyRewritten.Length, alreadyRewritten.Length)
                        .Equals(alreadyRewritten.AsSpan(), StringComparison.Ordinal))
                {
                    index++;
                    continue;
                }

                return index;
            }

            index++;
        }

        return -1;
    }

    private static string ExtractReceiver(string text, int dotIndex)
    {
        var depth = 0;
        var end = dotIndex;
        var pos = end - 1;

        while (pos >= 0)
        {
            var c = text[pos];
            if (c == ')' || c == ']')
            {
                depth++;
                pos--;
                continue;
            }

            if (c == '(' || c == '[')
            {
                depth--;
                if (depth < 0)
                {
                    break;
                }

                pos--;
                continue;
            }

            if (depth == 0)
            {
                if (c is ',' or '=' or ';' or '{' or '}' or '|' or '&' or '!' or '?')
                {
                    break;
                }

                if (char.IsWhiteSpace(c))
                {
                    var before = pos - 1;
                    while (before >= 0 && char.IsWhiteSpace(text[before]))
                    {
                        before--;
                    }

                    if (before < 0 || text[before] == ',' || text[before] == '=' || text[before] == ';' || text[before] == '(' || text[before] == '{')
                    {
                        break;
                    }
                }
            }

            pos--;
        }

        return text[(pos + 1)..end].Trim();
    }

    private static int FindMatchingCloseParen(string text, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < text.Length; i++)
        {
            if (text[i] == '"' || text[i] == '\'')
            {
                SkipQuotedString(text, ref i);
                continue;
            }

            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static void SkipQuotedString(string text, ref int index)
    {
        var quote = text[index];
        index++;
        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (text[index] == quote)
            {
                index++;
                return;
            }

            index++;
        }
    }

    private static void AppendInterpolatedString(StringBuilder builder, string source, ref int index, HashSet<string> locals)
    {
        builder.Append('$');
        index++;
        builder.Append('"');
        index++;
        var escaped = false;

        while (index < source.Length)
        {
            var character = source[index];
            if (IsSupportedDoubleQuoteDelimiter(character) && !escaped)
            {
                builder.Append('"');
                return;
            }

            if (character == '{' && !escaped)
            {
                if (index + 1 < source.Length && source[index + 1] == '{')
                {
                    builder.Append("{{");
                    index += 2;
                    escaped = false;
                    continue;
                }

                builder.Append('{');
                AppendInterpolatedHole(builder, source, ref index, locals);
                builder.Append('}');
                escaped = false;
                index++;
                continue;
            }

            if (character == '}' && index + 1 < source.Length && source[index + 1] == '}')
            {
                builder.Append("}}");
                index += 2;
                escaped = false;
                continue;
            }

            builder.Append(character);
            if (character == '\\' && !escaped)
            {
                escaped = true;
            }
            else
            {
                escaped = false;
            }

            index++;
        }
    }

    private static void AppendInterpolatedHole(StringBuilder builder, string source, ref int index, HashSet<string> locals)
    {
        var expression = new StringBuilder();
        var nestedBraceDepth = 0;
        var parenthesisDepth = 0;
        var bracketDepth = 0;
        var inString = false;
        var escaped = false;
        var quote = '\0';

        while (++index < source.Length)
        {
            var character = source[index];
            if (inString)
            {
                expression.Append(character);
                if (((IsSupportedDoubleQuoteDelimiter(quote) && IsSupportedDoubleQuoteDelimiter(character)) ||
                     (!IsSupportedDoubleQuoteDelimiter(quote) && character == quote)) &&
                    !escaped)
                {
                    inString = false;
                }

                if (character == '\\' && !escaped)
                {
                    escaped = true;
                }
                else
                {
                    escaped = false;
                }

                continue;
            }

            if (IsSupportedDoubleQuoteDelimiter(character) || character == '\'')
            {
                inString = true;
                escaped = false;
                quote = character;
                expression.Append(character);
                continue;
            }

            switch (character)
            {
                case '{':
                    nestedBraceDepth++;
                    expression.Append(character);
                    continue;
                case '}':
                    if (nestedBraceDepth == 0 && parenthesisDepth == 0 && bracketDepth == 0)
                    {
                        builder.Append(TranslateExpression(expression.ToString(), locals));
                        return;
                    }

                    if (nestedBraceDepth > 0)
                    {
                        nestedBraceDepth--;
                    }

                    expression.Append(character);
                    continue;
                case '(':
                    parenthesisDepth++;
                    expression.Append(character);
                    continue;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    expression.Append(character);
                    continue;
                case '[':
                    bracketDepth++;
                    expression.Append(character);
                    continue;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    expression.Append(character);
                    continue;
                default:
                    expression.Append(character);
                    continue;
            }
        }

        builder.Append(TranslateExpression(expression.ToString(), locals));
    }

    private static string NormalizeSendCalls(string expression)
    {
        var normalized = NormalizeLabeledSendCalls(expression);
        normalized = NormalizeAwaitableCall(normalized, "request.send");
        normalized = NormalizeAwaitableCall(normalized, "workspace.execute");
        return NormalizeAwaitableCall(normalized, "workspace.run");
    }

    private static string NormalizeLabeledSendCalls(string expression)
    {
        var pattern = new Regex(
            @"request\.send\(\)\s+as\s+""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return pattern.Replace(expression, match =>
        {
            var label = match.Groups[1].Value;
            return $"request.SendAsync({JsonSerializer.Serialize(label)})";
        });
    }

    private static string NormalizeAwaitableCall(string expression, string invocationName)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return expression;
        }

        var builder = new StringBuilder(expression.Length + 32);
        bool inString = false;
        bool escaped = false;
        char quote = '\0';

        for (int index = 0; index < expression.Length;)
        {
            char character = expression[index];
            if (inString)
            {
                builder.Append(character);
                if (((IsSupportedDoubleQuoteDelimiter(quote) && IsSupportedDoubleQuoteDelimiter(character))
                     || (!IsSupportedDoubleQuoteDelimiter(quote) && character == quote))
                    && !escaped)
                {
                    inString = false;
                    quote = '\0';
                }

                escaped = character == '\\' && !escaped;
                if (character != '\\')
                {
                    escaped = false;
                }

                index++;
                continue;
            }

            if (IsQuoteDelimiter(character))
            {
                inString = true;
                quote = character;
                escaped = false;
                builder.Append(character);
                index++;
                continue;
            }

            if (IsInvocationMatch(expression, index, invocationName)
                && TryFindAwaitableCallEnd(expression, index + invocationName.Length, out int endExclusive))
            {
                bool alreadyAwaited = IsAwaitedInvocation(expression, index);
                if (!alreadyAwaited)
                {
                    builder.Append("(await ");
                }

                builder.Append(expression, index, endExclusive - index);

                if (!alreadyAwaited)
                {
                    builder.Append(')');
                }

                index = endExclusive;
                continue;
            }

            builder.Append(character);
            index++;
        }

        return builder.ToString();
    }

    private static bool IsInvocationMatch(string expression, int index, string invocationName)
    {
        if (index > 0 && IsIdentifierCharacter(expression[index - 1]))
        {
            return false;
        }

        if (index + invocationName.Length > expression.Length
            || string.Compare(expression, index, invocationName, 0, invocationName.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        int nextIndex = index + invocationName.Length;
        if (nextIndex < expression.Length && IsIdentifierCharacter(expression[nextIndex]))
        {
            return false;
        }

        while (nextIndex < expression.Length && char.IsWhiteSpace(expression[nextIndex]))
        {
            nextIndex++;
        }

        return nextIndex < expression.Length && expression[nextIndex] == '(';
    }

    private static bool TryFindAwaitableCallEnd(string expression, int startIndex, out int endExclusive)
    {
        int openParenIndex = startIndex;
        while (openParenIndex < expression.Length && char.IsWhiteSpace(expression[openParenIndex]))
        {
            openParenIndex++;
        }

        if (openParenIndex >= expression.Length || expression[openParenIndex] != '(')
        {
            endExclusive = -1;
            return false;
        }

        int parenthesisDepth = 0;
        bool inString = false;
        bool escaped = false;
        char quote = '\0';

        for (int index = openParenIndex; index < expression.Length; index++)
        {
            char character = expression[index];
            if (inString)
            {
                if (((IsSupportedDoubleQuoteDelimiter(quote) && IsSupportedDoubleQuoteDelimiter(character))
                     || (!IsSupportedDoubleQuoteDelimiter(quote) && character == quote))
                    && !escaped)
                {
                    inString = false;
                    quote = '\0';
                }

                escaped = character == '\\' && !escaped;
                if (character != '\\')
                {
                    escaped = false;
                }

                continue;
            }

            if (IsQuoteDelimiter(character))
            {
                inString = true;
                quote = character;
                escaped = false;
                continue;
            }

            if (character == '(')
            {
                parenthesisDepth++;
                continue;
            }

            if (character == ')')
            {
                parenthesisDepth--;
                if (parenthesisDepth == 0)
                {
                    endExclusive = index + 1;
                    return true;
                }
            }
        }

        endExclusive = -1;
        return false;
    }

    private static bool IsAwaitedInvocation(string expression, int invocationIndex)
    {
        int endIndex = invocationIndex - 1;
        while (endIndex >= 0 && char.IsWhiteSpace(expression[endIndex]))
        {
            endIndex--;
        }

        if (endIndex < 0)
        {
            return false;
        }

        int startIndex = endIndex;
        while (startIndex >= 0 && IsIdentifierCharacter(expression[startIndex]))
        {
            startIndex--;
        }

        string token = expression[(startIndex + 1)..(endIndex + 1)];
        return string.Equals(token, "await", StringComparison.OrdinalIgnoreCase)
               && (startIndex < 0 || !IsIdentifierCharacter(expression[startIndex]));
    }

    private static bool IsIdentifierCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }

    private static bool IsQuoteDelimiter(char character)
    {
        return IsSupportedDoubleQuoteDelimiter(character) || character == '\'';
    }

    private static string RewriteKeywordBooleanOperators(string expression)
    {
        var rewritten = Regex.Replace(
            expression,
            @"\band\b",
            "&&",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        rewritten = Regex.Replace(
            rewritten,
            @"\bor\b",
            "||",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        rewritten = Regex.Replace(
            rewritten,
            @"\bnot\s+(?=\()",
            "!",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(
            rewritten,
            @"\bnot\s+(?<target>[A-Za-z_][A-Za-z0-9_\.\[\]]*)",
            static match => $"!{match.Groups["target"].Value}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string RewriteRangeLiterals(string expression)
    {
        return Regex.Replace(
            expression,
            @"\[\s*(?<start>-?\d+)\s*\.\.\s*(?<end>-?\d+)\s*\]",
            static match => $"__flow.RangeClosed({match.Groups["start"].Value}, {match.Groups["end"].Value})",
            RegexOptions.CultureInvariant);
    }

    private static string RewriteCountAliases(string expression)
    {
        var rewritten = expression;
        foreach (var alias in new[] { "length", "count", "size" })
        {
            rewritten = RewriteMemberFunctionAlias(rewritten, alias, "__flow.Count");
        }

        return rewritten;
    }

    private static string RewriteMemberFunctionAlias(string expression, string alias, string targetFunction)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return string.Empty;
        }

        string pattern = $".{alias}()";
        int searchIndex = 0;
        while (searchIndex < expression.Length)
        {
            int matchIndex = expression.IndexOf(pattern, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                break;
            }

            if (!TryFindMemberAliasExpressionStart(expression, matchIndex - 1, out int startIndex))
            {
                searchIndex = matchIndex + pattern.Length;
                continue;
            }

            string target = expression[startIndex..matchIndex].Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                searchIndex = matchIndex + pattern.Length;
                continue;
            }

            string replacement = $"{targetFunction}({target})";
            expression = string.Concat(expression.AsSpan(0, startIndex), replacement, expression.AsSpan(matchIndex + pattern.Length));
            searchIndex = startIndex + replacement.Length;
        }

        return expression;
    }

    private static bool TryFindMemberAliasExpressionStart(string expression, int endIndex, out int startIndex)
    {
        startIndex = 0;
        if (string.IsNullOrWhiteSpace(expression) || endIndex < 0 || endIndex >= expression.Length)
        {
            return false;
        }

        int parenthesisDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

        for (int index = endIndex; index >= 0; index--)
        {
            char character = expression[index];
            switch (character)
            {
                case ')':
                    parenthesisDepth++;
                    continue;
                case '(':
                    if (parenthesisDepth == 0)
                    {
                        startIndex = index + 1;
                        return true;
                    }

                    parenthesisDepth--;
                    continue;
                case ']':
                    bracketDepth++;
                    continue;
                case '[':
                    if (bracketDepth == 0)
                    {
                        startIndex = index + 1;
                        return true;
                    }

                    bracketDepth--;
                    continue;
                case '}':
                    braceDepth++;
                    continue;
                case '{':
                    if (braceDepth == 0)
                    {
                        startIndex = index + 1;
                        return true;
                    }

                    braceDepth--;
                    continue;
            }

            if (parenthesisDepth > 0 || bracketDepth > 0 || braceDepth > 0)
            {
                continue;
            }

            if (char.IsWhiteSpace(character) || IsExpressionBoundary(character))
            {
                startIndex = index + 1;
                return true;
            }
        }

        startIndex = 0;
        return true;
    }

    private static bool IsExpressionBoundary(char character)
    {
        return character is ',' or ';' or '=' or '+' or '-' or '*' or '/' or '%' or '!' or '<' or '>' or '&' or '|' or '^' or '?' or ':';
    }

    private static bool TryReportMalformedLegacyHelperCall(string trimmed, int index, List<ForRestScriptDiagnostic> diagnostics)
    {
        if (!LooksLikeLegacyHelperCall(trimmed))
        {
            return false;
        }

        string candidate = TrimStatement(trimmed);
        if (candidate.EndsWith(';'))
        {
            candidate = candidate[..^1].TrimEnd();
        }

        if (CountParenthesisBalance(candidate) == 0 && candidate.EndsWith(')'))
        {
            return false;
        }

        diagnostics.Add(
            new(
                ForRestScriptDiagnosticSeverity.Error,
                "Malformed legacy helper call. Close the call or rewrite it using ForRest syntax such as 'log ...', 'warn ...', 'error ...', 'request.headers[\"Name\"] = value', 'runtime key = value', 'request.send()', or 'workspace.execute(\"Request Name\")'.",
                index + 1,
                1));
        return true;
    }

    private static bool TryReportMisplacedExpectation(string statementText, int index, List<ForRestScriptDiagnostic> diagnostics)
    {
        if (!StartsWithKeywordBoundary(statementText.TrimStart(), "expect"))
        {
            return false;
        }

        diagnostics.Add(
            new(
                ForRestScriptDiagnosticSeverity.Error,
                "`expect` is a top-level assertion. Move it outside `if`, `else`, `foreach`, and `while` blocks or rewrite it as `tests.Assert(...)` / `tests.Equal(...)` inside flow.",
                index + 1,
                1));
        return true;
    }

    private static bool LooksLikeLegacyHelperCall(string value)
    {
        return value.StartsWith("console.Log(", StringComparison.Ordinal)
               || value.StartsWith("console.Warn(", StringComparison.Ordinal)
               || value.StartsWith("console.Error(", StringComparison.Ordinal)
               || value.StartsWith("request.SetHeader(", StringComparison.Ordinal)
               || value.StartsWith("variables.Set(", StringComparison.Ordinal)
               || value.StartsWith("await request.send(", StringComparison.Ordinal)
               || value.StartsWith("request.send(", StringComparison.Ordinal)
               || value.StartsWith("await workspace.execute(", StringComparison.Ordinal)
               || value.StartsWith("workspace.execute(", StringComparison.Ordinal)
               || value.StartsWith("await workspace.run(", StringComparison.Ordinal)
               || value.StartsWith("workspace.run(", StringComparison.Ordinal)
               || value.StartsWith("var sent = await request.send(", StringComparison.Ordinal)
               || value.StartsWith("let sent = await request.send(", StringComparison.Ordinal)
               || value.StartsWith("var sent = request.send(", StringComparison.Ordinal)
               || value.StartsWith("let sent = request.send(", StringComparison.Ordinal);
    }

    private static int CountParenthesisBalance(string value)
    {
        int balance = 0;
        bool inString = false;
        bool escaped = false;
        char quote = '\0';

        foreach (char character in value)
        {
            if (inString)
            {
                if (((IsSupportedDoubleQuoteDelimiter(quote) && IsSupportedDoubleQuoteDelimiter(character)) ||
                     (!IsSupportedDoubleQuoteDelimiter(quote) && character == quote)) &&
                    !escaped)
                {
                    inString = false;
                }

                if (character == '\\' && !escaped)
                {
                    escaped = true;
                }
                else
                {
                    escaped = false;
                }

                continue;
            }

            if (IsSupportedDoubleQuoteDelimiter(character) || character == '\'')
            {
                inString = true;
                escaped = false;
                quote = character;
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

    private static bool IsUnsupportedClassicForLoop(string trimmed)
    {
        return Regex.IsMatch(trimmed, @"^for\s*\(", RegexOptions.CultureInvariant);
    }

    private static void AppendNormalizedDoubleQuotedString(StringBuilder builder, string source, ref int index)
    {
        builder.Append('"');
        index++;
        var escaped = false;
        while (index < source.Length)
        {
            var character = source[index];
            if (IsSupportedDoubleQuoteDelimiter(character) && !escaped)
            {
                builder.Append('"');
                return;
            }

            builder.Append(character);
            if (character == '\\' && !escaped)
            {
                escaped = true;
            }
            else
            {
                escaped = false;
            }

            index++;
        }
    }

    private static void AppendSingleQuotedString(StringBuilder builder, string source, ref int index)
    {
        builder.Append(source[index]);
        index++;
        var escaped = false;
        while (index < source.Length)
        {
            var character = source[index];
            builder.Append(character);
            if (character == '\'' && !escaped)
            {
                return;
            }

            if (character == '\\' && !escaped)
            {
                escaped = true;
            }
            else
            {
                escaped = false;
            }

            index++;
        }
    }

    private static bool TryReadBlockHeader(
        IReadOnlyList<string> lines,
        int startIndex,
        string keyword,
        out string expression,
        out int consumedLineCount)
    {
        expression = string.Empty;
        if (!TryCollectHeaderText(lines, startIndex, out string combinedHeader, out consumedLineCount))
        {
            return false;
        }

        var withoutBrace = combinedHeader[..^1].TrimEnd();
        string remainder;
        if (string.Equals(keyword, "else if", StringComparison.Ordinal))
        {
            if (!withoutBrace.StartsWith("else", StringComparison.Ordinal))
            {
                return false;
            }

            remainder = withoutBrace[4..].TrimStart();
            if (remainder.Length < 2 || !remainder.StartsWith("if", StringComparison.Ordinal))
            {
                return false;
            }

            remainder = remainder[2..];
            if (remainder.Length == 0 || !(char.IsWhiteSpace(remainder[0]) || remainder[0] == '('))
            {
                return false;
            }
        }
        else
        {
            if (!withoutBrace.StartsWith(keyword, StringComparison.Ordinal))
            {
                return false;
            }

            remainder = withoutBrace[keyword.Length..];
            if (remainder.Length == 0 || !(char.IsWhiteSpace(remainder[0]) || remainder[0] == '('))
            {
                return false;
            }
        }

        expression = TrimOptionalOuterParentheses(remainder.Trim());
        return !string.IsNullOrWhiteSpace(expression);
    }

    private static bool TryReadForEachHeader(
        IReadOnlyList<string> lines,
        int startIndex,
        out string iteratorName,
        out string sourceExpression,
        out int consumedLineCount)
    {
        iteratorName = string.Empty;
        sourceExpression = string.Empty;
        if (!TryCollectHeaderText(lines, startIndex, out string combinedHeader, out consumedLineCount))
        {
            return false;
        }

        var withoutBrace = combinedHeader[..^1].TrimEnd();
        string header;
        if (withoutBrace.StartsWith("foreach", StringComparison.Ordinal))
        {
            var remainder = withoutBrace["foreach".Length..];
            if (remainder.Length == 0 || !(char.IsWhiteSpace(remainder[0]) || remainder[0] == '('))
            {
                return false;
            }

            header = remainder.Trim();
        }
        else if (withoutBrace.StartsWith("for", StringComparison.Ordinal))
        {
            var remainder = withoutBrace["for".Length..];
            if (remainder.Length == 0 || !(char.IsWhiteSpace(remainder[0]) || remainder[0] == '('))
            {
                return false;
            }

            header = remainder.Trim();
        }
        else
        {
            return false;
        }

        if (header.StartsWith('(') && header.EndsWith(')'))
        {
            header = header[1..^1].Trim();
        }

        var separatorIndex = header.IndexOf(" in ", StringComparison.Ordinal);
        if (separatorIndex < 1)
        {
            return false;
        }

        iteratorName = NormalizeForEachIterator(header[..separatorIndex]);
        sourceExpression = header[(separatorIndex + 4)..].Trim();
        return IsFlowIdentifier(iteratorName) && !string.IsNullOrWhiteSpace(sourceExpression);
    }

    private static bool IsElseBlockHeader(IReadOnlyList<string> lines, int startIndex, out int consumedLineCount)
    {
        consumedLineCount = 0;
        if (!TryCollectHeaderText(lines, startIndex, out string combinedHeader, out consumedLineCount))
        {
            return false;
        }

        return string.Equals(combinedHeader[..^1].TrimEnd(), "else", StringComparison.Ordinal);
    }

    private static void TryCollectStatementText(
        IReadOnlyList<string> lines,
        int startIndex,
        out string combinedText,
        out int consumedLineCount)
    {
        combinedText = string.Empty;
        consumedLineCount = 0;
        if (startIndex < 0 || startIndex >= lines.Count)
        {
            return;
        }

        string current = lines[startIndex].Trim();
        if (string.IsNullOrWhiteSpace(current) || current.StartsWith('#'))
        {
            return;
        }

        if (TryCollectStructuredLiteralAssignmentText(lines, startIndex, current, out combinedText, out consumedLineCount))
        {
            return;
        }

        StringBuilder builder = new(current);
        consumedLineCount = 1;

        while (startIndex + consumedLineCount < lines.Count)
        {
            string nextTrimmed = lines[startIndex + consumedLineCount].Trim();
            if (string.IsNullOrWhiteSpace(nextTrimmed) || nextTrimmed.StartsWith('#'))
            {
                break;
            }

            if (!ShouldContinueStatement(builder.ToString(), nextTrimmed))
            {
                break;
            }

            builder.Append(' ');
            builder.Append(nextTrimmed);
            consumedLineCount++;
        }

        combinedText = builder.ToString();
    }

    private static bool TryCollectStructuredLiteralAssignmentText(
        IReadOnlyList<string> lines,
        int startIndex,
        string current,
        out string combinedText,
        out int consumedLineCount)
    {
        combinedText = string.Empty;
        consumedLineCount = 0;

        if (!TryFindAssignmentIndex(current, out int separatorIndex))
        {
            return false;
        }

        string expression = TrimStatement(current[(separatorIndex + 1)..]);
        if (!string.IsNullOrWhiteSpace(expression) || startIndex + 1 >= lines.Count)
        {
            return false;
        }

        if (!TryCollectStructuredLiteral(lines, startIndex + 1, out string structuredLiteral, out int structuredLiteralLineCount))
        {
            return false;
        }

        combinedText = current + Environment.NewLine + structuredLiteral;
        consumedLineCount = 1 + structuredLiteralLineCount;
        return true;
    }

    private static bool TryCollectStructuredLiteral(
        IReadOnlyList<string> lines,
        int startIndex,
        out string structuredLiteral,
        out int consumedLineCount)
    {
        structuredLiteral = string.Empty;
        consumedLineCount = 0;
        if (startIndex < 0 || startIndex >= lines.Count)
        {
            return false;
        }

        int braceDepth = 0;
        int bracketDepth = 0;
        bool inString = false;
        bool escaped = false;
        char quote = '\0';
        bool sawStart = false;
        StringBuilder builder = new();

        for (int index = startIndex; index < lines.Count; index++)
        {
            string rawLine = lines[index];
            string trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (!sawStart)
                {
                    return false;
                }

                builder.AppendLine();
                consumedLineCount++;
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                return false;
            }

            if (!sawStart)
            {
                if (!IsStructuredLiteralStart(trimmed))
                {
                    return false;
                }

                sawStart = true;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(rawLine.TrimEnd());
            consumedLineCount++;

            foreach (char character in rawLine)
            {
                if (inString)
                {
                    if (character == quote && !escaped)
                    {
                        inString = false;
                        quote = '\0';
                    }

                    if (character == '\\' && !escaped)
                    {
                        escaped = true;
                    }
                    else
                    {
                        escaped = false;
                    }

                    continue;
                }

                if (IsSupportedDoubleQuoteDelimiter(character) || character == '\'')
                {
                    inString = true;
                    escaped = false;
                    quote = character;
                    continue;
                }

                if (character == '{')
                {
                    braceDepth++;
                }
                else if (character == '}')
                {
                    braceDepth--;
                }
                else if (character == '[')
                {
                    bracketDepth++;
                }
                else if (character == ']')
                {
                    bracketDepth--;
                }
            }

            if (!inString && braceDepth <= 0 && bracketDepth <= 0 && sawStart)
            {
                structuredLiteral = builder.ToString();
                return true;
            }
        }

        structuredLiteral = string.Empty;
        consumedLineCount = 0;
        return false;
    }

    private static bool IsStructuredLiteralStart(string trimmed)
    {
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        return trimmed[0] is '{' or '[';
    }

    private static bool TryParseStructuredLiteralNode(string source, out StructuredLiteralNode? node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        int index = 0;
        if (!TryParseStructuredLiteralNode(source, ref index, out node))
        {
            node = null;
            return false;
        }

        SkipWhitespace(source, ref index);
        return node is not null && index == source.Length;
    }

    private static bool TryParseStructuredLiteralNode(string source, ref int index, out StructuredLiteralNode? node)
    {
        node = null;
        SkipWhitespace(source, ref index);
        if (index >= source.Length)
        {
            return false;
        }

        return source[index] switch
        {
            '{' => TryParseStructuredObjectNode(source, ref index, out node),
            '[' => TryParseStructuredArrayNode(source, ref index, out node),
            _ => TryParseStructuredExpressionNode(source, ref index, out node),
        };
    }

    private static bool TryParseStructuredObjectNode(string source, ref int index, out StructuredLiteralNode? node)
    {
        node = null;
        if (index >= source.Length || source[index] != '{')
        {
            return false;
        }

        index++;
        List<StructuredLiteralProperty> properties = [];
        SkipWhitespace(source, ref index);
        if (index < source.Length && source[index] == '}')
        {
            index++;
            node = new StructuredLiteralObjectNode(properties);
            return true;
        }

        while (index < source.Length)
        {
            if (!TryParseStructuredLiteralProperty(source, ref index, out StructuredLiteralProperty? property) ||
                property is null)
            {
                return false;
            }

            properties.Add(property);
            SkipWhitespace(source, ref index);
            if (index >= source.Length)
            {
                return false;
            }

            if (source[index] == ',')
            {
                index++;
                SkipWhitespace(source, ref index);
                if (index < source.Length && source[index] == '}')
                {
                    index++;
                    node = new StructuredLiteralObjectNode(properties);
                    return true;
                }

                continue;
            }

            if (source[index] == '}')
            {
                index++;
                node = new StructuredLiteralObjectNode(properties);
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryParseStructuredArrayNode(string source, ref int index, out StructuredLiteralNode? node)
    {
        node = null;
        if (index >= source.Length || source[index] != '[')
        {
            return false;
        }

        index++;
        List<StructuredLiteralNode> items = [];
        SkipWhitespace(source, ref index);
        if (index < source.Length && source[index] == ']')
        {
            index++;
            node = new StructuredLiteralArrayNode(items);
            return true;
        }

        while (index < source.Length)
        {
            if (!TryParseStructuredLiteralNode(source, ref index, out StructuredLiteralNode? item) ||
                item is null)
            {
                return false;
            }

            items.Add(item);
            SkipWhitespace(source, ref index);
            if (index >= source.Length)
            {
                return false;
            }

            if (source[index] == ',')
            {
                index++;
                SkipWhitespace(source, ref index);
                if (index < source.Length && source[index] == ']')
                {
                    index++;
                    node = new StructuredLiteralArrayNode(items);
                    return true;
                }

                continue;
            }

            if (source[index] == ']')
            {
                index++;
                node = new StructuredLiteralArrayNode(items);
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryParseStructuredLiteralProperty(string source, ref int index, out StructuredLiteralProperty? property)
    {
        property = null;
        if (!TryReadStructuredLiteralKey(source, ref index, out string key))
        {
            return false;
        }

        SkipWhitespace(source, ref index);
        if (index >= source.Length || source[index] is not (':' or '='))
        {
            return false;
        }

        index++;
        if (!TryParseStructuredLiteralNode(source, ref index, out StructuredLiteralNode? value) || value is null)
        {
            return false;
        }

        property = new(key, value);
        return true;
    }

    private static bool TryParseStructuredExpressionNode(string source, ref int index, out StructuredLiteralNode? node)
    {
        node = null;
        if (!TryReadStructuredLiteralExpressionText(source, ref index, out string expression))
        {
            return false;
        }

        node = new StructuredLiteralExpressionNode(expression);
        return true;
    }

    private static bool TryReadStructuredLiteralKey(string source, ref int index, out string key)
    {
        key = string.Empty;
        SkipWhitespace(source, ref index);
        if (index >= source.Length)
        {
            return false;
        }

        if (TryReadQuotedToken(source, ref index, out key))
        {
            return true;
        }

        if (!IsIdentifierStart(source[index]))
        {
            return false;
        }

        int start = index;
        index++;
        while (index < source.Length && IsIdentifierPart(source[index]))
        {
            index++;
        }

        key = source[start..index];
        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool TryReadQuotedToken(string source, ref int index, out string value)
    {
        value = string.Empty;
        if (index >= source.Length)
        {
            return false;
        }

        char quote = source[index];
        if (!IsSupportedDoubleQuoteDelimiter(quote) && quote != '\'')
        {
            return false;
        }

        bool normalizeDoubleQuote = IsSupportedDoubleQuoteDelimiter(quote);
        index++;
        StringBuilder builder = new();
        bool escaped = false;

        while (index < source.Length)
        {
            char character = source[index];
            if ((normalizeDoubleQuote && IsSupportedDoubleQuoteDelimiter(character)) ||
                (!normalizeDoubleQuote && character == quote))
            {
                if (!escaped)
                {
                    index++;
                    value = builder.ToString();
                    return true;
                }
            }

            builder.Append(character);
            if (character == '\\' && !escaped)
            {
                escaped = true;
            }
            else
            {
                escaped = false;
            }

            index++;
        }

        return false;
    }

    private static bool TryReadStructuredLiteralExpressionText(string source, ref int index, out string expression)
    {
        expression = string.Empty;
        SkipWhitespace(source, ref index);
        if (index >= source.Length)
        {
            return false;
        }

        int start = index;
        int parenthesisDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        bool inString = false;
        bool escaped = false;
        char quote = '\0';

        while (index < source.Length)
        {
            char character = source[index];
            if (inString)
            {
                if (((IsSupportedDoubleQuoteDelimiter(quote) && IsSupportedDoubleQuoteDelimiter(character)) ||
                     (!IsSupportedDoubleQuoteDelimiter(quote) && character == quote)) &&
                    !escaped)
                {
                    inString = false;
                    quote = '\0';
                }

                if (character == '\\' && !escaped)
                {
                    escaped = true;
                }
                else
                {
                    escaped = false;
                }

                index++;
                continue;
            }

            if (IsSupportedDoubleQuoteDelimiter(character) || character == '\'')
            {
                inString = true;
                quote = character;
                escaped = false;
                index++;
                continue;
            }

            switch (character)
            {
                case '(':
                    parenthesisDepth++;
                    index++;
                    continue;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    index++;
                    continue;
                case '[':
                    bracketDepth++;
                    index++;
                    continue;
                case ']':
                    if (bracketDepth == 0 && braceDepth == 0 && parenthesisDepth == 0)
                    {
                        goto Finish;
                    }

                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    index++;
                    continue;
                case '{':
                    braceDepth++;
                    index++;
                    continue;
                case '}':
                    if (braceDepth == 0 && bracketDepth == 0 && parenthesisDepth == 0)
                    {
                        goto Finish;
                    }

                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    index++;
                    continue;
                case ',':
                    if (braceDepth == 0 && bracketDepth == 0 && parenthesisDepth == 0)
                    {
                        goto Finish;
                    }

                    index++;
                    continue;
                default:
                    index++;
                    continue;
            }
        }

    Finish:
        expression = TrimStatement(source[start..index]);
        return !string.IsNullOrWhiteSpace(expression);
    }

    private static void SkipWhitespace(string source, ref int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }
    }

    private static string RenderStructuredLiteralNode(StructuredLiteralNode node, HashSet<string> locals)
    {
        return node switch
        {
            StructuredLiteralObjectNode objectNode => RenderStructuredObjectNode(objectNode, locals),
            StructuredLiteralArrayNode arrayNode => RenderStructuredArrayNode(arrayNode, locals),
            StructuredLiteralExpressionNode expressionNode => RenderStructuredLiteralLeaf(expressionNode.Expression, locals),
            _ => throw new InvalidOperationException("Unsupported structured literal node."),
        };
    }

    private static string RenderStructuredObjectNode(StructuredLiteralObjectNode node, HashSet<string> locals)
    {
        if (node.Properties.Count == 0)
        {
            return "new JsonObject()";
        }

        StringBuilder builder = new("new JsonObject { ");
        for (int index = 0; index < node.Properties.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            StructuredLiteralProperty property = node.Properties[index];
            builder.Append('[');
            builder.Append(RenderString(property.Key));
            builder.Append("] = ");
            builder.Append(RenderStructuredLiteralNode(property.Value, locals));
        }

        builder.Append(" }");
        return builder.ToString();
    }

    private static string RenderStructuredArrayNode(StructuredLiteralArrayNode node, HashSet<string> locals)
    {
        if (node.Items.Count == 0)
        {
            return "new JsonArray()";
        }

        StringBuilder builder = new("new JsonArray { ");
        for (int index = 0; index < node.Items.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(RenderStructuredLiteralNode(node.Items[index], locals));
        }

        builder.Append(" }");
        return builder.ToString();
    }

    private static string RenderStructuredLiteralLeaf(string expression, HashSet<string> locals)
    {
        return $"__flow.J({TranslateExpression(expression, locals)})";
    }

    private static bool TryCollectHeaderText(
        IReadOnlyList<string> lines,
        int startIndex,
        out string combinedHeader,
        out int consumedLineCount)
    {
        combinedHeader = string.Empty;
        consumedLineCount = 0;
        if (startIndex < 0 || startIndex >= lines.Count)
        {
            return false;
        }

        StringBuilder builder = new();
        for (int index = startIndex; index < lines.Count; index++)
        {
            string trimmed = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(trimmed);
            consumedLineCount++;

            if (trimmed == "{" || trimmed.EndsWith('{'))
            {
                combinedHeader = builder.ToString();
                return true;
            }
        }

        consumedLineCount = 0;
        combinedHeader = string.Empty;
        return false;
    }

    private static bool ShouldContinueStatement(string currentStatement, string nextTrimmed)
    {
        if (string.IsNullOrWhiteSpace(nextTrimmed) || LooksLikeStandaloneFlowStatement(nextTrimmed))
        {
            return false;
        }

        return StatementNeedsContinuation(currentStatement) || StartsWithContinuationToken(nextTrimmed);
    }

    private static bool LooksLikeStandaloneFlowStatement(string trimmed)
    {
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed is "{" or "}" ||
            trimmed.EndsWith('{') ||
            trimmed.StartsWith("else", StringComparison.Ordinal))
        {
            return true;
        }

        return StartsWithKeywordBoundary(trimmed, "if", allowOpenParenStart: true) ||
               StartsWithKeywordBoundary(trimmed, "while", allowOpenParenStart: true) ||
               StartsWithKeywordBoundary(trimmed, "foreach", allowOpenParenStart: true) ||
               StartsWithKeywordBoundary(trimmed, "for", allowOpenParenStart: true) ||
               StartsWithKeywordBoundary(trimmed, "let") ||
               StartsWithKeywordBoundary(trimmed, "runtime") ||
               StartsWithKeywordBoundary(trimmed, "log", allowOpenParenStart: true) ||
               StartsWithKeywordBoundary(trimmed, "warn", allowOpenParenStart: true) ||
               StartsWithKeywordBoundary(trimmed, "error", allowOpenParenStart: true) ||
               StartsWithKeywordBoundary(trimmed, "delay", allowOpenParenStart: true) ||
               string.Equals(trimmed, "break", StringComparison.Ordinal) ||
               string.Equals(trimmed, "continue", StringComparison.Ordinal) ||
               TryFindAssignmentIndex(trimmed, out _);
    }

    private static bool StatementNeedsContinuation(string statement)
    {
        string trimmed = TrimStatement(statement);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (HasUnbalancedDelimiters(trimmed) || EndsWithContinuationOperator(trimmed))
        {
            return true;
        }

        if (TryFindAssignmentIndex(trimmed, out int separatorIndex))
        {
            string expression = TrimStatement(trimmed[(separatorIndex + 1)..]);
            return string.IsNullOrWhiteSpace(expression);
        }

        return string.Equals(trimmed, "log", StringComparison.Ordinal) ||
               string.Equals(trimmed, "warn", StringComparison.Ordinal) ||
               string.Equals(trimmed, "error", StringComparison.Ordinal) ||
               string.Equals(trimmed, "delay", StringComparison.Ordinal);
    }

    private static bool StartsWithContinuationToken(string trimmed)
    {
        return trimmed.StartsWith(".", StringComparison.Ordinal) ||
               trimmed.StartsWith("?.", StringComparison.Ordinal) ||
               trimmed.StartsWith("??", StringComparison.Ordinal) ||
               trimmed.StartsWith("&&", StringComparison.Ordinal) ||
               trimmed.StartsWith("||", StringComparison.Ordinal) ||
               trimmed.StartsWith("==", StringComparison.Ordinal) ||
               trimmed.StartsWith("!=", StringComparison.Ordinal) ||
               trimmed.StartsWith("<=", StringComparison.Ordinal) ||
               trimmed.StartsWith(">=", StringComparison.Ordinal) ||
               trimmed.StartsWith("=", StringComparison.Ordinal) ||
               trimmed.StartsWith("+", StringComparison.Ordinal) ||
               trimmed.StartsWith("-", StringComparison.Ordinal) ||
               trimmed.StartsWith("*", StringComparison.Ordinal) ||
               trimmed.StartsWith("/", StringComparison.Ordinal) ||
               trimmed.StartsWith("%", StringComparison.Ordinal) ||
               trimmed.StartsWith(",", StringComparison.Ordinal) ||
               trimmed.StartsWith(":", StringComparison.Ordinal) ||
               Regex.IsMatch(trimmed, "^(and|or)\\b", RegexOptions.IgnoreCase);
    }

    private static bool EndsWithContinuationOperator(string statement)
    {
        string trimmed = TrimStatement(statement);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        return trimmed.EndsWith("?.", StringComparison.Ordinal) ||
               trimmed.EndsWith("??", StringComparison.Ordinal) ||
               trimmed.EndsWith("&&", StringComparison.Ordinal) ||
               trimmed.EndsWith("||", StringComparison.Ordinal) ||
               trimmed.EndsWith("==", StringComparison.Ordinal) ||
               trimmed.EndsWith("!=", StringComparison.Ordinal) ||
               trimmed.EndsWith("<=", StringComparison.Ordinal) ||
               trimmed.EndsWith(">=", StringComparison.Ordinal) ||
               trimmed.EndsWith("=", StringComparison.Ordinal) ||
               trimmed.EndsWith("+", StringComparison.Ordinal) ||
               trimmed.EndsWith("-", StringComparison.Ordinal) ||
               trimmed.EndsWith("*", StringComparison.Ordinal) ||
               trimmed.EndsWith("/", StringComparison.Ordinal) ||
               trimmed.EndsWith("%", StringComparison.Ordinal) ||
               trimmed.EndsWith(",", StringComparison.Ordinal) ||
               trimmed.EndsWith(":", StringComparison.Ordinal) ||
               trimmed.EndsWith(".", StringComparison.Ordinal) ||
               Regex.IsMatch(trimmed, "(^|\\s)(and|or)$", RegexOptions.IgnoreCase);
    }

    private static bool HasUnbalancedDelimiters(string text)
    {
        int parenthesesDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        bool inString = false;
        bool escaped = false;
        char quote = '\0';

        foreach (char character in text)
        {
            if (inString)
            {
                if (((IsSupportedDoubleQuoteDelimiter(quote) && IsSupportedDoubleQuoteDelimiter(character)) ||
                     (!IsSupportedDoubleQuoteDelimiter(quote) && character == quote)) &&
                    !escaped)
                {
                    inString = false;
                }

                if (character == '\\' && !escaped)
                {
                    escaped = true;
                }
                else
                {
                    escaped = false;
                }

                continue;
            }

            if (IsSupportedDoubleQuoteDelimiter(character) || character == '\'')
            {
                inString = true;
                escaped = false;
                quote = character;
                continue;
            }

            switch (character)
            {
                case '(':
                    parenthesesDepth++;
                    break;
                case ')':
                    parenthesesDepth = Math.Max(0, parenthesesDepth - 1);
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    braceDepth = Math.Max(0, braceDepth - 1);
                    break;
            }
        }

        return inString || parenthesesDepth > 0 || bracketDepth > 0 || braceDepth > 0;
    }

    private static bool StartsWithKeywordBoundary(string text, string keyword, bool allowOpenParenStart = false)
    {
        if (!text.StartsWith(keyword, StringComparison.Ordinal))
        {
            return false;
        }

        if (text.Length == keyword.Length)
        {
            return true;
        }

        char nextCharacter = text[keyword.Length];
        return char.IsWhiteSpace(nextCharacter) || (allowOpenParenStart && nextCharacter == '(');
    }

    private static bool TrySplitAssignment(string source, out string name, out string expression)
    {
        name = string.Empty;
        expression = string.Empty;
        if (!TryFindAssignmentIndex(source, out int separatorIndex) || separatorIndex < 1)
        {
            return false;
        }

        name = source[..separatorIndex].Trim();
        expression = TrimStatement(source[(separatorIndex + 1)..]);
        return IsFlowIdentifier(name) && !string.IsNullOrWhiteSpace(expression);
    }

    private static bool TryFindAssignmentIndex(string source, out int separatorIndex)
    {
        separatorIndex = -1;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        bool inString = false;
        bool escaped = false;
        char quote = '\0';

        for (int index = 0; index < source.Length; index++)
        {
            char character = source[index];
            if (inString)
            {
                if (((IsSupportedDoubleQuoteDelimiter(quote) && IsSupportedDoubleQuoteDelimiter(character)) ||
                     (!IsSupportedDoubleQuoteDelimiter(quote) && character == quote)) &&
                    !escaped)
                {
                    inString = false;
                }

                if (character == '\\' && !escaped)
                {
                    escaped = true;
                }
                else
                {
                    escaped = false;
                }

                continue;
            }

            if (IsSupportedDoubleQuoteDelimiter(character) || character == '\'')
            {
                inString = true;
                escaped = false;
                quote = character;
                continue;
            }

            if (character != '=')
            {
                continue;
            }

            char previous = index > 0 ? source[index - 1] : '\0';
            char next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (previous is '=' or '!' or '<' or '>' || next is '=' or '>')
            {
                continue;
            }

            separatorIndex = index;
            return true;
        }

        return false;
    }

    private static string TrimStatement(string text)
    {
        return (text ?? string.Empty).Trim().TrimEnd(';').Trim();
    }

    private static string NormalizeForEachIterator(string iteratorText)
    {
        var trimmed = iteratorText.Trim();
        foreach (var prefix in new[] { "var ", "let ", "dynamic " })
        {
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }

        return trimmed;
    }

    private static string RewriteOutsideQuotedText(string source, Func<string, string> transform)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(source.Length);
        var segment = new StringBuilder();
        var inString = false;
        var escaped = false;
        var quote = '\0';
        var normalizeDoubleQuote = false;

        foreach (var character in source)
        {
            if (inString)
            {
                if ((normalizeDoubleQuote && IsSupportedDoubleQuoteDelimiter(character)) ||
                    (!normalizeDoubleQuote && character == quote))
                {
                    builder.Append(normalizeDoubleQuote ? '"' : character);
                    if (!escaped)
                    {
                        inString = false;
                    }
                }
                else
                {
                    builder.Append(character);
                }

                escaped = character == '\\' && !escaped;
                if (character != '\\')
                {
                    escaped = false;
                }

                continue;
            }

            if (IsSupportedDoubleQuoteDelimiter(character) || character == '\'')
            {
                if (segment.Length > 0)
                {
                    builder.Append(transform(segment.ToString()));
                    segment.Clear();
                }

                normalizeDoubleQuote = IsSupportedDoubleQuoteDelimiter(character);
                builder.Append(normalizeDoubleQuote ? '"' : character);
                inString = true;
                quote = character;
                escaped = false;
                continue;
            }

            segment.Append(character);
        }

        if (segment.Length > 0)
        {
            builder.Append(transform(segment.ToString()));
        }

        return builder.ToString();
    }

    private static bool IsSupportedDoubleQuoteDelimiter(char character)
    {
        return character is '"' or '\u201C' or '\u201D' or '\u201E' or '\u00AB' or '\u00BB';
    }

    private static bool TryReadKeywordRemainder(string text, string keyword, out string remainder, bool allowOpenParenStart = false)
    {
        remainder = string.Empty;
        if (!text.StartsWith(keyword, StringComparison.Ordinal))
        {
            return false;
        }

        if (text.Length == keyword.Length)
        {
            return false;
        }

        var nextCharacter = text[keyword.Length];
        if (!char.IsWhiteSpace(nextCharacter) && !(allowOpenParenStart && nextCharacter == '('))
        {
            return false;
        }

        remainder = TrimStatement(text[keyword.Length..]);
        return !string.IsNullOrWhiteSpace(remainder);
    }

    private static string TrimOptionalOuterParentheses(string text)
    {
        var trimmed = text.Trim();
        while (IsWrappedByOuterParentheses(trimmed))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    private static bool IsWrappedByOuterParentheses(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] != '(' || text[^1] != ')')
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        var quote = '\0';

        for (int index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (((IsSupportedDoubleQuoteDelimiter(quote) && IsSupportedDoubleQuoteDelimiter(character)) ||
                     (!IsSupportedDoubleQuoteDelimiter(quote) && character == quote)) &&
                    !escaped)
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

            if (IsSupportedDoubleQuoteDelimiter(character) || character == '\'')
            {
                inString = true;
                quote = character;
                escaped = false;
                continue;
            }

            if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
                if (depth == 0 && index < text.Length - 1)
                {
                    return false;
                }
            }
        }

        return depth == 0;
    }

    private static bool IsFlowIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsIdentifierStart(value[0]))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!IsIdentifierPart(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    private static string Normalize(string source)
    {
        return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private abstract record StructuredLiteralNode;

    private sealed record StructuredLiteralObjectNode(IReadOnlyList<StructuredLiteralProperty> Properties) : StructuredLiteralNode;

    private sealed record StructuredLiteralArrayNode(IReadOnlyList<StructuredLiteralNode> Items) : StructuredLiteralNode;

    private sealed record StructuredLiteralExpressionNode(string Expression) : StructuredLiteralNode;

    private sealed record StructuredLiteralProperty(string Key, StructuredLiteralNode Value);

    private static string RenderString(string value)
    {
        return JsonSerializer.Serialize(value);
    }
}

public sealed class ForRestFlowRuntime(VariablesApi variables)
{
    public dynamic V(string key)
    {
        var value = variables.Get(key, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (bool.TryParse(value, out var booleanValue))
        {
            return booleanValue;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return decimalValue;
        }

        if (Guid.TryParse(value, out var guidValue))
        {
            return guidValue;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateValue))
        {
            return dateValue;
        }

        try
        {
            return DynamicJsonObject.Wrap(JsonNode.Parse(value)) ?? value;
        }
        catch (JsonException)
        {
            return value;
        }
    }

    public IEnumerable<int> Range(int startInclusive, int endExclusive)
    {
        var count = Math.Max(0, endExclusive - startInclusive);
        return Enumerable.Range(startInclusive, count);
    }

    public IEnumerable<int> RangeClosed(int startInclusive, int endInclusive)
    {
        if (startInclusive <= endInclusive)
        {
            return Enumerable.Range(startInclusive, (endInclusive - startInclusive) + 1);
        }

        return Enumerable.Range(endInclusive, (startInclusive - endInclusive) + 1).Reverse();
    }

    public int Count(object? value)
    {
        return value switch
        {
            null => 0,
            string stringValue => stringValue.Length,
            ScriptResponseApi response => Count(response.Json() ?? response.Body),
            JsonArray jsonArray => jsonArray.Count,
            JsonObject jsonObject => jsonObject.Count,
            JsonNode jsonNode => jsonNode.ToJsonString().Length,
            ICollection collection => collection.Count,
            IEnumerable enumerable => enumerable.Cast<object?>().Count(),
            _ => value.ToString()?.Length ?? 0,
        };
    }

    public string S(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string stringValue => stringValue,
            bool booleanValue => booleanValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            JsonNode jsonNode => jsonNode.ToJsonString(),
            _ => value.ToString() ?? string.Empty,
        };
    }

    public JsonNode? J(object? value)
    {
        return value switch
        {
            null => null,
            JsonNode jsonNode => CloneNode(jsonNode),
            ScriptResponseApi response => response.Json() ?? JsonValue.Create(response.Body),
            string stringValue => JsonValue.Create(stringValue),
            char charValue => JsonValue.Create(charValue.ToString()),
            bool boolValue => JsonValue.Create(boolValue),
            int intValue => JsonValue.Create(intValue),
            long longValue => JsonValue.Create(longValue),
            decimal decimalValue => JsonValue.Create(decimalValue),
            double doubleValue => JsonValue.Create(doubleValue),
            float floatValue => JsonValue.Create(floatValue),
            Guid guidValue => JsonValue.Create(guidValue.ToString()),
            DateTimeOffset dateTimeOffsetValue => JsonValue.Create(dateTimeOffsetValue.ToString("O", CultureInfo.InvariantCulture)),
            _ => TrySerializeToJsonNode(value) ?? JsonValue.Create(S(value)),
        };
    }

    public void Bind(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        string rootKey = key.Trim();
        variables.ClearRuntimeNamespace(rootKey);
        foreach ((string bindingKey, string bindingValue) in FlattenBindings(rootKey, value))
        {
            variables.Set(bindingKey, bindingValue);
        }
    }

    private IEnumerable<(string Key, string Value)> FlattenBindings(string rootKey, object? value)
    {
        switch (value)
        {
            case null:
                yield return (rootKey, string.Empty);
                yield break;
            case ScriptResponseApi response:
                JsonNode? responseJson = response.Json();
                yield return (rootKey, responseJson?.ToJsonString() ?? response.Body);
                yield return ($"{rootKey}.status", response.Status.ToString(CultureInfo.InvariantCulture));
                yield return ($"{rootKey}.body", response.Body);
                if (!string.IsNullOrWhiteSpace(response.ContentType))
                {
                    yield return ($"{rootKey}.content_type", response.ContentType);
                }

                foreach (KeyValuePair<string, string> header in response.Headers.Where(static item => !string.IsNullOrWhiteSpace(item.Key)))
                {
                    yield return ($"{rootKey}.headers.{header.Key}", header.Value ?? string.Empty);
                }

                if (responseJson is not null)
                {
                    foreach ((string nestedKey, string nestedValue) in FlattenJsonBindings(rootKey, responseJson))
                    {
                        yield return (nestedKey, nestedValue);
                    }
                }

                yield break;
            case JsonNode jsonNode:
                yield return (rootKey, jsonNode.ToJsonString());
                foreach ((string nestedKey, string nestedValue) in FlattenJsonBindings(rootKey, jsonNode))
                {
                    yield return (nestedKey, nestedValue);
                }

                yield break;
            default:
                JsonNode? serializedNode = TrySerializeToJsonNode(value);
                if (serializedNode is not null)
                {
                    yield return (rootKey, serializedNode.ToJsonString());
                    foreach ((string nestedKey, string nestedValue) in FlattenJsonBindings(rootKey, serializedNode))
                    {
                        yield return (nestedKey, nestedValue);
                    }

                    yield break;
                }

                yield return (rootKey, S(value));
                yield break;
        }
    }

    private IEnumerable<(string Key, string Value)> FlattenJsonBindings(string rootKey, JsonNode node)
    {
        if (node is not JsonObject jsonObject)
        {
            yield break;
        }

        foreach ((string propertyName, JsonNode? propertyValue) in jsonObject)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                continue;
            }

            string nestedKey = $"{rootKey}.{propertyName}";
            switch (propertyValue)
            {
                case null:
                    yield return (nestedKey, string.Empty);
                    break;
                case JsonValue jsonValue:
                    yield return (nestedKey, JsonScalarToString(jsonValue));
                    break;
                case JsonObject childObject:
                    yield return (nestedKey, childObject.ToJsonString());
                    foreach ((string childKey, string childValue) in FlattenJsonBindings(nestedKey, childObject))
                    {
                        yield return (childKey, childValue);
                    }
                    break;
                case JsonArray jsonArray:
                    yield return (nestedKey, jsonArray.ToJsonString());
                    break;
            }
        }
    }

    private static string JsonScalarToString(JsonValue value)
    {
        if (value.TryGetValue<string>(out string? stringValue))
        {
            return stringValue ?? string.Empty;
        }

        if (value.TryGetValue<bool>(out bool boolValue))
        {
            return boolValue ? "true" : "false";
        }

        if (value.TryGetValue<int>(out int intValue))
        {
            return intValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetValue<long>(out long longValue))
        {
            return longValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetValue<decimal>(out decimal decimalValue))
        {
            return decimalValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetValue<double>(out double doubleValue))
        {
            return doubleValue.ToString(CultureInfo.InvariantCulture);
        }

        return value.ToJsonString().Trim('"');
    }

    private static JsonNode? CloneNode(JsonNode? node)
    {
        return node is null ? null : JsonNode.Parse(node.ToJsonString());
    }

    private static JsonNode? TrySerializeToJsonNode(object value)
    {
        try
        {
            return value switch
            {
                string => null,
                char => null,
                IFormattable => null,
                _ => JsonSerializer.SerializeToNode(value),
            };
        }
        catch
        {
            return null;
        }
    }
}
