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
        "continue",
        "__flow",
        "console",
        "Convert",
        "crypto",
        "DateTimeOffset",
        "dynamic",
        "else",
        "encoding",
        "Encoding",
        "Enumerable",
        "Environment",
        "false",
        "Guid",
        "if",
        "in",
        "int",
        "Math",
        "let",
        "long",
        "new",
        "null",
        "object",
        "regex",
        "Regex",
        "request",
        "response",
        "runtime",
        "string",
        "StringComparison",
        "stash",
        "tests",
        "time",
        "true",
        "Uri",
        "var",
        "variables",
        "while",
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
    ];

    public static string Compile(
        string flowSource,
        IEnumerable<string> knownVariableNames,
        List<ForRestScriptDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(flowSource))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("var __flow = new ForRest.Scripting.ForRestFlowRuntime(variables);");

        var knownIdentifiers = knownVariableNames
            .Where(IsFlowIdentifier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var identifier in knownIdentifiers)
        {
            builder.Append("dynamic ");
            builder.Append(identifier);
            builder.Append(" = __flow.V(");
            builder.Append(RenderString(identifier));
            builder.AppendLine(");");
        }

        var lines = Normalize(flowSource).Split('\n');
        var index = 0;
        var tempCounter = 0;
        CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(knownIdentifiers, StringComparer.OrdinalIgnoreCase), ref tempCounter, allowBlockTerminator: false);
        return builder.ToString().Trim();
    }

    private static void CompileBlock(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
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

            if (TryCompileIfStatement(builder, lines, ref index, diagnostics, locals, ref tempCounter))
            {
                continue;
            }

            if (TryCompileWhileStatement(builder, lines, ref index, diagnostics, locals, ref tempCounter))
            {
                continue;
            }

            if (TryCompileForEachStatement(builder, lines, ref index, diagnostics, locals, ref tempCounter))
            {
                continue;
            }

            if (IsUnsupportedClassicForLoop(trimmed))
            {
                diagnostics.Add(new(ForRestScriptDiagnosticSeverity.Error, "Classic C-style for loops are not supported in .frs flow. Use 'foreach item in range(...) { }' or 'while ... { }'.", index + 1, 1));
                index++;
                continue;
            }

            if (TryCompileLetStatement(builder, trimmed, locals, ref tempCounter))
            {
                index++;
                continue;
            }

            if (TryCompileRuntimeStatement(builder, trimmed, locals, ref tempCounter))
            {
                index++;
                continue;
            }

            if (TryCompileLogStatement(builder, trimmed, "Log", locals))
            {
                index++;
                continue;
            }

            if (TryCompileLogStatement(builder, trimmed, "Warn", locals))
            {
                index++;
                continue;
            }

            if (TryCompileLogStatement(builder, trimmed, "Error", locals))
            {
                index++;
                continue;
            }

            if (TryReportMalformedLegacyHelperCall(trimmed, index, diagnostics))
            {
                index++;
                continue;
            }

            if (trimmed.EndsWith('{'))
            {
                builder.AppendLine(TranslateRawStatement(trimmed, locals));
                index++;
                CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), ref tempCounter, allowBlockTerminator: true);
                builder.AppendLine("}");
                continue;
            }

            builder.AppendLine(TranslateRawStatement(trimmed, locals));
            index++;
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
        CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), ref tempCounter, allowBlockTerminator: true);
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
                CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), ref tempCounter, allowBlockTerminator: true);
                builder.AppendLine("}");
                continue;
            }

            if (IsElseBlockHeader(lines, index, out consumedLineCount))
            {
                builder.AppendLine("else {");
                index += consumedLineCount;
                CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), ref tempCounter, allowBlockTerminator: true);
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
        CompileBlock(builder, lines, ref index, diagnostics, new HashSet<string>(locals, StringComparer.OrdinalIgnoreCase), ref tempCounter, allowBlockTerminator: true);
        builder.AppendLine("}");
        return true;
    }

    private static bool TryCompileForEachStatement(
        StringBuilder builder,
        IReadOnlyList<string> lines,
        ref int index,
        List<ForRestScriptDiagnostic> diagnostics,
        HashSet<string> locals,
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
        CompileBlock(builder, lines, ref index, diagnostics, nestedLocals, ref tempCounter, allowBlockTerminator: true);
        builder.AppendLine("}");
        return true;
    }

    private static bool TryCompileLetStatement(
        StringBuilder builder,
        string trimmed,
        HashSet<string> locals,
        ref int tempCounter)
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

    private static string TranslateRawStatement(string statement, HashSet<string> locals)
    {
        var trimmed = TrimStatement(statement);
        var suffix = statement.TrimEnd().EndsWith('{') ? " {" : ";";
        if (suffix == " {")
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        return TranslateExpression(trimmed, locals) + suffix;
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
                segment = NormalizeSendCalls(segment);
                segment = RewriteKeywordBooleanOperators(segment);
                segment = RewriteRangeLiterals(segment);
                segment = RewriteCountAliases(segment);
                segment = segment.Replace("range(", "__flow.Range(", StringComparison.OrdinalIgnoreCase);
                segment = segment.Replace("random(", "random.Number(", StringComparison.OrdinalIgnoreCase);
                return segment;
            });

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

        return builder.ToString();
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
        const string awaitSendSentinel = "__forrest_await_request_send__";
        var normalized = Regex.Replace(
            expression,
            @"await\s+request\.send\s*\(\s*\)",
            awaitSendSentinel,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            @"request\.send\s*\(\s*\)",
            "(await request.send())",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return normalized.Replace(awaitSendSentinel, "(await request.send())", StringComparison.Ordinal);
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
                "Malformed legacy helper call. Close the call or rewrite it using ForRest syntax such as 'log ...', 'warn ...', 'error ...', 'request.headers[\"Name\"] = value', 'runtime key = value', or 'request.send()'.",
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

    private static bool TrySplitAssignment(string source, out string name, out string expression)
    {
        name = string.Empty;
        expression = string.Empty;
        var separatorIndex = source.IndexOf('=');
        if (separatorIndex < 1)
        {
            return false;
        }

        name = source[..separatorIndex].Trim();
        expression = TrimStatement(source[(separatorIndex + 1)..]);
        return IsFlowIdentifier(name) && !string.IsNullOrWhiteSpace(expression);
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
}
