using System.Text;
using System.Text.RegularExpressions;

namespace ForRest.Services.AI;

public sealed record AiInlineConversationRequest(
    string DocumentId,
    string DocumentTitle,
    string Language,
    string SourceText,
    int CursorLineNumber,
    AiSettings Settings,
    IAiActiveDocumentHost ActiveDocumentHost);

public sealed record AiInlineConversationResult(
    bool Handled,
    bool Succeeded,
    string UpdatedText,
    string StatusText,
    string DebugText,
    string ResponseText = "",
    int? PromptLineNumber = null,
    AiInlineConversationUpdateKind UpdateKind = AiInlineConversationUpdateKind.ResponseOnly,
    int? SuggestedCursorLineNumber = null,
    int SuggestedCursorColumn = 4)
{
    public static AiInlineConversationResult NotHandled(string sourceText)
    {
        return new(false, true, sourceText ?? string.Empty, string.Empty, string.Empty);
    }
}

public enum AiInlineConversationUpdateKind
{
    ResponseOnly,
    DocumentChanged,
}

public interface IAiInlineConversationService
{
    Task<AiInlineConversationResult> TryHandleAsync(AiInlineConversationRequest request, CancellationToken cancellationToken = default);
}

public sealed class AiInlineConversationService : IAiInlineConversationService
{
    private const int MaxAutonomousRepairAttempts = 1;
    private const int PromptCompactionMinLength = 1400;
    private const int DeterministicCrudMinSendIterations = 8;
    private const string RestfulApiObjectsBaseUrl = "https://api.restful-api.dev/objects";
    private const string DeterministicCrudRewriteResponseText = "Applied the built-in CRUD rewrite for the restful-api.dev objects API surface.";
    private static readonly Regex PromptUrlRegex = new(
        @"https?://[^\s""'`)\]]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PromptMethodUrlRegex = new(
        @"^(?<method>GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s+(?<url>https?://\S+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RequestUrlDirectiveRegex = new(
        @"(?im)^\s*url\s+""(?<url>[^""]+)""",
        RegexOptions.Compiled);
    private static readonly Regex HeaderDirectiveNameRegex = new(
        @"^\s*header\s+""(?<name>[^""]+)""\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RuntimeDirectiveNameRegex = new(
        @"^\s*runtime\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MaxSendIterationsDirectiveRegex = new(
        @"^\s*max_send_iterations\s+(?<value>\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> PromptHttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "HEAD",
        "OPTIONS",
    };
    private static readonly string[] PromptDocMarkerPhrases =
    [
        "description",
        "parameters",
        "headers",
        "response body example",
        "request body example",
        "request url example",
        "list of all objects",
        "single object",
        "add a new object",
        "update an object",
        "partially update an object",
        "delete an object",
        "pathrequired",
        "query",
    ];
    private static readonly string[] StuckResponsePhrases =
    [
        "can't apply",
        "cannot apply",
        "requested change in this canvas",
        "can't safely",
        "cannot safely",
        "can't update",
        "cannot update",
        "can't rewrite",
        "cannot rewrite",
        "rejected by the editor",
        "not runnable",
        "the runtime rejected",
        "runtime rejected",
        "diagnostics indicate",
        "one quick detail",
        "without knowing",
        "need to know",
        "which exact",
        "if you're good with that",
        "if you want me to",
        "not sure",
        "unclear",
        "i can't",
        "i cannot",
        "i tried to rewrite",
        "can't parse",
        "cannot parse",
        "what i need from you",
        "are you trying to",
        "reply 1",
        "reply 2",
        "reply \"1\"",
        "reply \"2\"",
    ];
    private static readonly string[] ForcedReplacementResponsePhrases =
    [
        "still not updated",
        "too brittle",
        "incremental patching safely",
        "replace the entire active document",
        "replace the whole document",
        "replace the whole script",
        "replace the entire document",
        "replace the whole request",
        "if you allow one action",
        "safe version",
        "in-place patch",
        "full replacements were rejected",
        "earlier full replacements were rejected",
    ];
    private readonly IAiTurnExecutor _turnExecutor;

    public AiInlineConversationService(IAiTurnExecutor turnExecutor)
    {
        _turnExecutor = turnExecutor;
    }

    public async Task<AiInlineConversationResult> TryHandleAsync(AiInlineConversationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ActiveDocumentHost);

        AiInlineConversationDocument document = AiInlineConversationParser.Parse(request.SourceText);
        AiInlineConversationPrompt? prompt = AiInlineConversationPromptResolver.ResolveActionablePrompt(document, request.CursorLineNumber);
        if (prompt is not null)
        {
            AiInlineConversationResult? commandResult = TryHandlePromptCommand(request.SourceText, prompt);
            if (commandResult is not null)
            {
                return commandResult;
            }
        }

        DeterministicCrudCandidate? deterministicCrudCandidate = FindDeterministicRestfulCrudRewriteCandidate(
            document,
            request);
        AiInlineConversationPrompt? responsePrompt = prompt ?? deterministicCrudCandidate?.Prompt;
        if (deterministicCrudCandidate is not null &&
            responsePrompt is not null &&
            TryApplyDeterministicRestfulCrudRewrite(
                request,
                sessionReset: false,
                autonomousEditRecoveryAttempts: 0,
                out AiTurnExecutionResult deterministicTurn))
        {
            string deterministicLatestSource = request.ActiveDocumentHost.GetActiveDocument()?.SourceText ?? request.SourceText;
            ConversationUpdate deterministicUpdatedDocument = BuildUpdatedDocument(
                request.SourceText,
                deterministicLatestSource,
                responsePrompt,
                deterministicTurn);

            return new(
                Handled: true,
                Succeeded: deterministicTurn.Succeeded,
                UpdatedText: deterministicUpdatedDocument.Text,
                StatusText: BuildStatusText(deterministicTurn),
                DebugText: BuildDebugText(
                    request,
                    responsePrompt,
                    PreparePromptForExecution(request, responsePrompt.PromptText),
                    deterministicCrudCandidate,
                    deterministicLatestSource,
                    deterministicTurn),
                ResponseText: deterministicTurn.ResponseText,
                PromptLineNumber: responsePrompt.LineNumber,
                UpdateKind: deterministicUpdatedDocument.Kind,
                SuggestedCursorLineNumber: deterministicUpdatedDocument.SuggestedCursorLineNumber,
                SuggestedCursorColumn: deterministicUpdatedDocument.SuggestedCursorColumn);
        }

        if (prompt is null)
        {
            return AiInlineConversationResult.NotHandled(request.SourceText);
        }

        PreparedPrompt preparedPrompt = PreparePromptForExecution(request, prompt.PromptText);
        AiTurnExecutionResult turn;
        turn = await ExecuteTurnWithAutonomousRecoveryAsync(request, prompt, preparedPrompt, cancellationToken);
        string latestSourceAfterTurn = request.ActiveDocumentHost.GetActiveDocument()?.SourceText ?? request.SourceText;
        if (deterministicCrudCandidate is not null &&
            !turn.Succeeded &&
            !HasDocumentChanged(request.SourceText, latestSourceAfterTurn) &&
            TryApplyDeterministicRestfulCrudRewrite(
                request,
                turn.SessionReset,
                turn.AutonomousEditRecoveryAttempts,
                out AiTurnExecutionResult recoveredTurn))
        {
            turn = recoveredTurn;
            latestSourceAfterTurn = request.ActiveDocumentHost.GetActiveDocument()?.SourceText ?? request.SourceText;
        }

        string latestSource = latestSourceAfterTurn;
        ConversationUpdate updatedDocument = BuildUpdatedDocument(request.SourceText, latestSource, prompt, turn);

        return new(
            Handled: true,
            Succeeded: turn.Succeeded,
            UpdatedText: updatedDocument.Text,
            StatusText: BuildStatusText(turn),
            DebugText: BuildDebugText(
                request,
                prompt,
                preparedPrompt,
                deterministicCrudCandidate,
                latestSource,
                turn),
            ResponseText: turn.ResponseText,
            PromptLineNumber: prompt.LineNumber,
            UpdateKind: updatedDocument.Kind,
            SuggestedCursorLineNumber: updatedDocument.SuggestedCursorLineNumber,
            SuggestedCursorColumn: updatedDocument.SuggestedCursorColumn);
    }

    private static AiInlineConversationResult? TryHandlePromptCommand(string sourceText, AiInlineConversationPrompt prompt)
    {
        string commandText = NormalizePromptCommandText(prompt.PromptText);
        if (string.Equals(commandText, "reset", StringComparison.OrdinalIgnoreCase))
        {
            string cleanedSource = RemoveConversationBlocks(sourceText);
            ConversationUpdate updatedResetDocument = InsertFreshPromptNearOriginalConversation(
                cleanedSource,
                sourceText,
                prompt.LineNumber,
                AiInlineConversationUpdateKind.DocumentChanged);

            return new(
                Handled: true,
                Succeeded: true,
                UpdatedText: updatedResetDocument.Text,
                StatusText: "AI history reset.",
                DebugText: BuildCommandDebugText(prompt, "reset"),
                PromptLineNumber: updatedResetDocument.SuggestedCursorLineNumber,
                UpdateKind: updatedResetDocument.Kind,
                SuggestedCursorLineNumber: updatedResetDocument.SuggestedCursorLineNumber,
                SuggestedCursorColumn: updatedResetDocument.SuggestedCursorColumn);
        }

        if (string.Equals(commandText, "clear responses", StringComparison.OrdinalIgnoreCase))
        {
            ConversationUpdate updatedDocument = ClearResponsesAndReopenPrompt(sourceText, prompt);

            return new(
                Handled: true,
                Succeeded: true,
                UpdatedText: updatedDocument.Text,
                StatusText: "Inline AI responses cleared.",
                DebugText: BuildCommandDebugText(prompt, "clear responses"),
                PromptLineNumber: updatedDocument.SuggestedCursorLineNumber,
                UpdateKind: updatedDocument.Kind,
                SuggestedCursorLineNumber: updatedDocument.SuggestedCursorLineNumber,
                SuggestedCursorColumn: updatedDocument.SuggestedCursorColumn);
        }

        if (string.Equals(commandText, "collapse", StringComparison.OrdinalIgnoreCase))
        {
            ConversationUpdate updatedDocument = CollapseConversationHistory(sourceText, prompt);

            return new(
                Handled: true,
                Succeeded: true,
                UpdatedText: updatedDocument.Text,
                StatusText: "Inline AI history collapsed.",
                DebugText: BuildCommandDebugText(prompt, "collapse"),
                PromptLineNumber: updatedDocument.SuggestedCursorLineNumber,
                UpdateKind: updatedDocument.Kind,
                SuggestedCursorLineNumber: updatedDocument.SuggestedCursorLineNumber,
                SuggestedCursorColumn: updatedDocument.SuggestedCursorColumn);
        }

        if (string.Equals(commandText, "help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandText, "commands", StringComparison.OrdinalIgnoreCase))
        {
            string withResponse = AiInlineConversationFormatter.ApplyResponse(
                sourceText,
                prompt.LineNumber,
                BuildPromptCommandHelpText());
            string latestOnly = KeepOnlyPromptBlock(withResponse, prompt.LineNumber);
            ConversationUpdate updatedHelpDocument = InsertFreshPromptAfterConversation(
                latestOnly,
                prompt.LineNumber,
                AiInlineConversationUpdateKind.ResponseOnly);

            return new(
                Handled: true,
                Succeeded: true,
                UpdatedText: updatedHelpDocument.Text,
                StatusText: "Listed AI prompt commands.",
                DebugText: BuildCommandDebugText(prompt, "help"),
                ResponseText: BuildPromptCommandHelpText(),
                PromptLineNumber: prompt.LineNumber,
                UpdateKind: updatedHelpDocument.Kind,
                SuggestedCursorLineNumber: updatedHelpDocument.SuggestedCursorLineNumber,
                SuggestedCursorColumn: updatedHelpDocument.SuggestedCursorColumn);
        }

        return null;
    }

    private async Task<AiTurnExecutionResult> ExecuteTurnWithAutonomousRecoveryAsync(
        AiInlineConversationRequest request,
        AiInlineConversationPrompt prompt,
        PreparedPrompt preparedPrompt,
        CancellationToken cancellationToken)
    {
        AiTurnExecutionResult turn = await ExecuteTurnAsync(request, preparedPrompt.EffectivePrompt, cancellationToken);
        bool sessionReset = turn.SessionReset;

        for (int attempt = 0; attempt < MaxAutonomousRepairAttempts; attempt++)
        {
            string latestSource = request.ActiveDocumentHost.GetActiveDocument()?.SourceText ?? request.SourceText;
            if (!ShouldAttemptAutonomousRepair(preparedPrompt.RawPrompt, request.SourceText, latestSource, turn))
            {
                return turn with { SessionReset = sessionReset };
            }

            AiTurnExecutionResult repairTurn = await ExecuteTurnAsync(
                request,
                BuildAutonomousRepairPrompt(preparedPrompt.EffectivePrompt, turn.ResponseText, attempt + 1),
                cancellationToken);
            sessionReset |= repairTurn.SessionReset;
            turn = repairTurn with { SessionReset = sessionReset };
        }

        string finalSource = request.ActiveDocumentHost.GetActiveDocument()?.SourceText ?? request.SourceText;
        if (ShouldAttemptAutonomousRepair(preparedPrompt.RawPrompt, request.SourceText, finalSource, turn))
        {
            return turn with
            {
                Succeeded = false,
                SessionReset = sessionReset,
            };
        }

        return turn with { SessionReset = sessionReset };
    }

    private static bool IsDeterministicRestfulCrudRewriteCandidate(PreparedPrompt preparedPrompt)
    {
        if (!preparedPrompt.WasCompacted)
        {
            return false;
        }

        List<string> operations = ExtractApiOperations(preparedPrompt.RawPrompt);
        if (operations.Count < 5 ||
            !HasMethod(operations, "GET") ||
            !HasMethod(operations, "POST") ||
            !HasMethod(operations, "PUT") ||
            !HasMethod(operations, "PATCH") ||
            !HasMethod(operations, "DELETE") ||
            operations.Any(static operation => !IsRestfulApiObjectsOperation(operation)))
        {
            return false;
        }

        string normalizedPrompt = NormalizeSearchText(preparedPrompt.RawPrompt);
        bool requestsCrudCoverage =
            normalizedPrompt.Contains("api surface", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("fully test", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("full api surface", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("test this api", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("test the api", StringComparison.Ordinal);

        return requestsCrudCoverage &&
               normalizedPrompt.Contains("stash", StringComparison.Ordinal) &&
               normalizedPrompt.Contains("restful-api.dev/objects", StringComparison.Ordinal);
    }

    private static bool IsRestfulApiObjectsOperation(string operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            return false;
        }

        string[] segments = operation.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        string url = TrimExtractedUrl(segments[1]);
        return url.StartsWith(RestfulApiObjectsBaseUrl, StringComparison.OrdinalIgnoreCase);
    }

    private static DeterministicCrudCandidate? FindDeterministicRestfulCrudRewriteCandidate(
        AiInlineConversationDocument document,
        AiInlineConversationRequest request)
    {
        int cursorLineNumber = request.CursorLineNumber <= 0
            ? int.MaxValue
            : request.CursorLineNumber;

        foreach (AiInlineConversationPrompt candidatePrompt in document.Prompts
                     .Where(prompt => prompt.LineNumber <= cursorLineNumber)
                     .Reverse())
        {
            if (string.IsNullOrWhiteSpace(candidatePrompt.PromptText))
            {
                continue;
            }

            PreparedPrompt preparedPrompt = PreparePromptForExecution(request, candidatePrompt.PromptText);
            if (IsDeterministicRestfulCrudRewriteCandidate(preparedPrompt))
            {
                return new(candidatePrompt, preparedPrompt);
            }
        }

        return null;
    }

    private static bool TryApplyDeterministicRestfulCrudRewrite(
        AiInlineConversationRequest request,
        bool sessionReset,
        int autonomousEditRecoveryAttempts,
        out AiTurnExecutionResult turn)
    {
        turn = new(
            Succeeded: false,
            ResponseText: string.Empty,
            Issues: [],
            SessionReset: sessionReset,
            AutonomousEditRecoveryAttempts: autonomousEditRecoveryAttempts);

        AiActiveDocumentSnapshot? document = request.ActiveDocumentHost.GetActiveDocument();
        if (document is null)
        {
            return false;
        }

        string rewrittenSource = BuildDeterministicRestfulCrudRewriteSource(document.SourceText, request.DocumentTitle);
        AiActiveDocumentUpdateResult updateResult = request.ActiveDocumentHost.UpdateActiveDocument(document, rewrittenSource);
        if (!updateResult.Succeeded)
        {
            return false;
        }

        turn = new(
            Succeeded: true,
            ResponseText: DeterministicCrudRewriteResponseText,
            Issues: [],
            SessionReset: sessionReset,
            AutonomousEditRecoveryAttempts: autonomousEditRecoveryAttempts,
            DeterministicFallbackApplied: true);
        return true;
    }

    private static string BuildDeterministicRestfulCrudRewriteSource(string sourceText, string documentTitle)
    {
        string[] sourceLines = ExtractDeterministicCrudDirectiveSourceLines(sourceText);

        string resolvedTitle = string.IsNullOrWhiteSpace(documentTitle) ? "restful-api QA" : NormalizeLineEndings(documentTitle).Replace('\n', ' ').Trim();
        string nameLine = TryFindDirectiveLine(sourceLines, "name") ?? $"name {RenderQuotedValue(resolvedTitle)}";
        string timeoutLine = TryFindDirectiveLine(sourceLines, "timeout") ?? "timeout 15000";
        int maxSendIterations = Math.Max(
            DeterministicCrudMinSendIterations,
            ParseDirectiveInt(sourceLines, MaxSendIterationsDirectiveRegex, DeterministicCrudMinSendIterations));
        string redirectsLine = TryFindDirectiveLine(sourceLines, "redirects") ?? "redirects true";
        string sslLine = TryFindDirectiveLine(sourceLines, "ssl") ?? "ssl true";
        string historyLine = TryFindDirectiveLine(sourceLines, "history") ?? "history true";
        List<string> runtimeLines = CollectDirectiveLines(sourceLines, "runtime");
        EnsureRuntimeDirective(runtimeLines, "trace_id", "runtime trace_id = guid()");
        List<string> headerLines = CollectDirectiveLines(sourceLines, "header");
        EnsureHeaderDirective(headerLines, "Accept", "header \"Accept\" = \"application/json\"", insertAtStart: true);
        EnsureHeaderDirective(headerLines, "X-Correlation-Id", "header \"X-Correlation-Id\" = \"{{trace_id}}\"");

        List<string> lines =
        [
            nameLine,
            "method GET",
            $"url {RenderQuotedValue(RestfulApiObjectsBaseUrl)}",
            timeoutLine,
            $"max_send_iterations {maxSendIterations}",
            redirectsLine,
            sslLine,
            historyLine,
            string.Empty,
            .. runtimeLines,
            .. headerLines
        ];

        if (runtimeLines.Count > 0 || headerLines.Count > 0)
        {
            lines.Add(string.Empty);
        }

        lines.AddRange(BuildDeterministicRestfulCrudFlowLines());
        return JoinLines(CollapseBlankLines(lines), "\n");
    }

    private static string[] ExtractDeterministicCrudDirectiveSourceLines(string sourceText)
    {
        List<string> lines = [];
        foreach (string rawLine in NormalizeLineEndings(RemoveConversationBlocks(sourceText)).Split('\n', StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith('#'))
            {
                continue;
            }

            if (!IsDeterministicCrudPreservedDirectiveLine(line))
            {
                break;
            }

            lines.Add(line);
        }

        return [.. lines];
    }

    private static bool IsDeterministicCrudPreservedDirectiveLine(string line)
    {
        return line.StartsWith("name ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("method ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("url ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("timeout ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("max_send_iterations ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("redirects ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("ssl ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("history ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("header ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("runtime ", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryFindDirectiveLine(IEnumerable<string> sourceLines, string keyword)
    {
        return sourceLines
            .Reverse()
            .FirstOrDefault(line => line.StartsWith($"{keyword} ", StringComparison.OrdinalIgnoreCase));
    }

    private static int ParseDirectiveInt(IEnumerable<string> sourceLines, Regex regex, int fallbackValue)
    {
        foreach (string line in sourceLines.Reverse())
        {
            Match match = regex.Match(line);
            if (match.Success &&
                int.TryParse(match.Groups["value"].Value, out int parsedValue))
            {
                return parsedValue;
            }
        }

        return fallbackValue;
    }

    private static List<string> CollectDirectiveLines(IEnumerable<string> sourceLines, string keyword)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> directives = [];
        foreach (string line in sourceLines)
        {
            if (!line.StartsWith($"{keyword} ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string trimmed = line.Trim();
            if (seen.Add(trimmed))
            {
                directives.Add(trimmed);
            }
        }

        return directives;
    }

    private static void EnsureRuntimeDirective(List<string> runtimeLines, string variableName, string directiveLine)
    {
        if (runtimeLines.Any(line => string.Equals(TryParseRuntimeDirectiveName(line), variableName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        runtimeLines.Add(directiveLine);
    }

    private static void EnsureHeaderDirective(List<string> headerLines, string headerName, string directiveLine, bool insertAtStart = false)
    {
        if (headerLines.Any(line => string.Equals(TryParseHeaderDirectiveName(line), headerName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (insertAtStart)
        {
            headerLines.Insert(0, directiveLine);
            return;
        }

        headerLines.Add(directiveLine);
    }

    private static string? TryParseHeaderDirectiveName(string line)
    {
        Match match = HeaderDirectiveNameRegex.Match(line ?? string.Empty);
        return match.Success ? match.Groups["name"].Value.Trim() : null;
    }

    private static string? TryParseRuntimeDirectiveName(string line)
    {
        Match match = RuntimeDirectiveNameRegex.Match(line ?? string.Empty);
        return match.Success ? match.Groups["name"].Value.Trim() : null;
    }

    private static string RenderQuotedValue(string value)
    {
        string escaped = NormalizeLineEndings(value).Replace('\n', ' ')
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static IReadOnlyList<string> CollapseBlankLines(IEnumerable<string> lines)
    {
        List<string> collapsed = [];
        bool previousWasBlank = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine ?? string.Empty;
            bool isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank)
            {
                if (previousWasBlank)
                {
                    continue;
                }

                collapsed.Add(string.Empty);
                previousWasBlank = true;
                continue;
            }

            collapsed.Add(line.TrimEnd());
            previousWasBlank = false;
        }

        while (collapsed.Count > 0 && string.IsNullOrWhiteSpace(collapsed[^1]))
        {
            collapsed.RemoveAt(collapsed.Count - 1);
        }

        return collapsed;
    }

    private static IReadOnlyList<string> BuildDeterministicRestfulCrudFlowLines()
    {
        return NormalizeLineEndings(
            """
            let created_name = $"ForRest Widget {trace_id}"
            let patched_name = $"ForRest Widget Updated {trace_id}"

            log $"Trace {trace_id}: GET /objects"
            request.method = "GET"
            request.url = "https://api.restful-api.dev/objects"
            let sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "list returns 2xx")
            tests.Assert(sent.length() >= 3, "list returns at least 3 objects")
            let sample_id_a = convert.ToString(sent[0].id)
            let sample_id_b = convert.ToString(sent[1].id)
            let sample_id_c = convert.ToString(sent[2].id)
            stash.Step = "list"
            stash.Status = sent.status
            stash.Count = sent.length()
            stash.SampleIds = $"{sample_id_a},{sample_id_b},{sample_id_c}"
            stash.Trace = trace_id
            stash.Commit()

            log $"Trace {trace_id}: GET /objects?id=..."
            request.url = $"https://api.restful-api.dev/objects?id={sample_id_a}&id={sample_id_b}&id={sample_id_c}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "filtered list returns 2xx")
            tests.Equal(3, sent.length(), "filtered list returns requested ids")
            stash.Step = "filtered-list"
            stash.Status = sent.status
            stash.Count = sent.length()
            stash.FirstId = sent[0].id
            stash.Commit()

            log $"Trace {trace_id}: GET /objects/{sample_id_c}"
            request.url = $"https://api.restful-api.dev/objects/{sample_id_c}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "single object returns 2xx")
            tests.Equal(sample_id_c, convert.ToString(sent.id), "single object returns requested id")
            stash.Step = "single"
            stash.Status = sent.status
            stash.ObjectId = sent.id
            stash.Name = sent.name
            stash.Commit()

            log $"Trace {trace_id}: POST /objects"
            request.method = "POST"
            request.url = "https://api.restful-api.dev/objects"
            request.content_type = "application/json"
            request.body = $"{{\"name\":\"{created_name}\",\"data\":{{\"year\":2026,\"price\":1849.99,\"CPU model\":\"Trace CPU\",\"Hard disk size\":\"1 TB\"}}}}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "create returns 2xx")
            tests.Equal(created_name, convert.ToString(sent.name), "create echoes name")
            let created_id = sent.id
            stash.Step = "create"
            stash.Status = sent.status
            stash.ObjectId = created_id
            stash.Name = sent.name
            stash.CreatedAt = sent.createdAt
            stash.Commit()

            log $"Trace {trace_id}: PUT /objects/{created_id}"
            request.method = "PUT"
            request.url = $"https://api.restful-api.dev/objects/{created_id}"
            request.body = $"{{\"name\":\"{created_name}\",\"data\":{{\"year\":2026,\"price\":2049.99,\"CPU model\":\"Trace CPU\",\"Hard disk size\":\"1 TB\",\"color\":\"silver\"}}}}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "put returns 2xx")
            tests.Equal(2049.99, convert.ToDouble(sent.data.price), "put replaces price")
            tests.Equal("silver", convert.ToString(sent.data.color), "put adds color")
            stash.Step = "put"
            stash.Status = sent.status
            stash.ObjectId = sent.id
            stash.Price = sent.data.price
            stash.Color = sent.data.color
            stash.UpdatedAt = sent.updatedAt
            stash.Commit()

            log $"Trace {trace_id}: PATCH /objects/{created_id}"
            request.method = "PATCH"
            request.url = $"https://api.restful-api.dev/objects/{created_id}"
            request.body = $"{{\"name\":\"{patched_name}\"}}"
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "patch returns 2xx")
            tests.Equal(patched_name, convert.ToString(sent.name), "patch updates name")
            stash.Step = "patch"
            stash.Status = sent.status
            stash.ObjectId = sent.id
            stash.Name = sent.name
            stash.UpdatedAt = sent.updatedAt
            stash.Commit()

            log $"Trace {trace_id}: DELETE /objects/{created_id}"
            request.method = "DELETE"
            request.url = $"https://api.restful-api.dev/objects/{created_id}"
            request.body = ""
            sent = request.send()
            tests.Assert(sent.status >= 200 and sent.status < 300, "delete returns 2xx")
            tests.Assert(strings.Contains(convert.ToString(sent.message), convert.ToString(created_id)), "delete message includes id")
            stash.Step = "delete"
            stash.Status = sent.status
            stash.ObjectId = created_id
            stash.DeleteMessage = sent.message
            stash.Commit()

            expect status == 200 "final delete returns 200"
            expect header "Content-Type" contains "json" "json response"
            """)
            .Split('\n');
    }

    private Task<AiTurnExecutionResult> ExecuteTurnAsync(
        AiInlineConversationRequest request,
        string promptText,
        CancellationToken cancellationToken)
    {
        return _turnExecutor.ExecuteAsync(
            new(
                ConversationId: request.DocumentId,
                Objective: BuildObjective(request),
                Prompt: promptText,
                Settings: request.Settings,
                ActiveDocumentHost: request.ActiveDocumentHost),
            cancellationToken);
    }

    private static ConversationUpdate BuildUpdatedDocument(
        string originalSource,
        string latestSource,
        AiInlineConversationPrompt originalPrompt,
        AiTurnExecutionResult turn)
    {
        bool documentChanged = turn.Succeeded && HasDocumentChanged(originalSource, latestSource);
        bool promptStillExists = TryResolvePromptLineNumber(latestSource, originalPrompt, out int promptLineNumber);
        if (documentChanged && !promptStillExists)
        {
            string cleaned = RemoveConversationBlocks(latestSource);
            return InsertFreshPromptNearOriginalConversation(
                cleaned,
                originalSource,
                originalPrompt.LineNumber,
                AiInlineConversationUpdateKind.DocumentChanged);
        }

        int responsePromptLineNumber = promptStillExists ? promptLineNumber : originalPrompt.LineNumber;
        string responseSource = promptStillExists ? latestSource : originalSource;
        string withResponse = AiInlineConversationFormatter.ApplyResponse(responseSource, responsePromptLineNumber, turn.ResponseText);
        string latestOnly = KeepOnlyPromptBlock(withResponse, responsePromptLineNumber);
        return InsertFreshPromptAfterConversation(latestOnly, responsePromptLineNumber, AiInlineConversationUpdateKind.ResponseOnly);
    }

    private static bool TryResolvePromptLineNumber(string sourceText, AiInlineConversationPrompt originalPrompt, out int promptLineNumber)
    {
        AiInlineConversationDocument current = AiInlineConversationParser.Parse(sourceText);
        if (current.Prompts.Count == 0)
        {
            promptLineNumber = originalPrompt.LineNumber;
            return false;
        }

        AiInlineConversationPrompt? match = current.Prompts
            .Where(prompt => string.Equals(prompt.PromptText, originalPrompt.PromptText, StringComparison.Ordinal))
            .OrderBy(prompt => Math.Abs(prompt.LineNumber - originalPrompt.LineNumber))
            .FirstOrDefault();
        promptLineNumber = match?.LineNumber ?? current.FindLatestPrompt(originalPrompt.LineNumber)?.LineNumber ?? originalPrompt.LineNumber;
        int resolvedPromptLineNumber = promptLineNumber;
        return current.Prompts.Any(prompt => prompt.LineNumber == resolvedPromptLineNumber);
    }

    private static bool HasDocumentChanged(string originalSource, string latestSource)
    {
        return !string.Equals(
            NormalizeLineEndings(AiInlineConversationFormatter.RemoveConversationLines(originalSource)).Trim(),
            NormalizeLineEndings(AiInlineConversationFormatter.RemoveConversationLines(latestSource)).Trim(),
            StringComparison.Ordinal);
    }

    private static bool ShouldAttemptAutonomousRepair(
        string promptText,
        string originalSource,
        string latestSource,
        AiTurnExecutionResult turn)
    {
        bool likelyEditPrompt = AiPromptIntentClassifier.IsLikelyEditPrompt(promptText);
        bool documentChanged = HasDocumentChanged(originalSource, latestSource);
        if (turn.ResponseText.StartsWith("AI request failed:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (turn.ResponseText.StartsWith("AI request timed out", StringComparison.OrdinalIgnoreCase) ||
            turn.ResponseText.StartsWith("AI request canceled", StringComparison.OrdinalIgnoreCase))
        {
            return likelyEditPrompt &&
                   !documentChanged &&
                   turn.AutonomousEditRecoveryAttempts == 0;
        }
        if (likelyEditPrompt &&
            !documentChanged &&
            turn.AutonomousEditRecoveryAttempts > 0)
        {
            return false;
        }

        if (likelyEditPrompt && LooksLikeIncompleteEditResponse(turn.ResponseText))
        {
            return true;
        }

        if (!documentChanged && LooksLikeStuckResponse(turn.ResponseText))
        {
            return true;
        }

        return !turn.Succeeded && likelyEditPrompt;
    }

    private static bool LooksLikeStuckResponse(string responseText)
    {
        string normalized = NormalizeSearchText(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.EndsWith("?", StringComparison.Ordinal) ||
               ContainsAnyPhrase(normalized, StuckResponsePhrases) ||
               LooksLikeRejectedEditResponse(normalized) ||
               LooksLikeMultipleChoiceResponse(normalized);
    }

    private static bool LooksLikeRejectedEditResponse(string normalizedResponse)
    {
        if (string.IsNullOrWhiteSpace(normalizedResponse))
        {
            return false;
        }

        if (normalizedResponse.Contains("editor rejected", StringComparison.Ordinal) ||
            normalizedResponse.Contains("rejected by the editor", StringComparison.Ordinal) ||
            normalizedResponse.Contains("rejected my update", StringComparison.Ordinal) ||
            normalizedResponse.Contains("requested change in this canvas", StringComparison.Ordinal) ||
            normalizedResponse.Contains("tried to replace", StringComparison.Ordinal) ||
            normalizedResponse.Contains("tried to patch", StringComparison.Ordinal) ||
            normalizedResponse.Contains("retry by patching", StringComparison.Ordinal) ||
            normalizedResponse.Contains("retry by replacing", StringComparison.Ordinal) ||
            normalizedResponse.Contains("not runnable", StringComparison.Ordinal) ||
            normalizedResponse.Contains("need the active request to be editable", StringComparison.Ordinal))
        {
            return true;
        }

        bool mentionsRuntimeRejectingScript =
            normalizedResponse.Contains("runtime is currently rejecting the script", StringComparison.Ordinal) ||
            normalizedResponse.Contains("currently rejecting the script", StringComparison.Ordinal);
        bool mentionsFailedActiveDocumentUpdate =
            normalizedResponse.Contains("did not successfully modify the active document", StringComparison.Ordinal) ||
            normalizedResponse.Contains("did not modify the active document", StringComparison.Ordinal);
        bool mentionsAttemptedUpdate =
            normalizedResponse.Contains("attempted to update the active request", StringComparison.Ordinal) ||
            normalizedResponse.Contains("attempted to update the request", StringComparison.Ordinal);
        bool mentionsStillTargetsPreviousRequest =
            normalizedResponse.Contains("still targets", StringComparison.Ordinal) ||
            normalizedResponse.Contains("still points to", StringComparison.Ordinal) ||
            normalizedResponse.Contains("still not updated", StringComparison.Ordinal);
        if (mentionsRuntimeRejectingScript ||
            mentionsFailedActiveDocumentUpdate ||
            (mentionsAttemptedUpdate && mentionsStillTargetsPreviousRequest))
        {
            return true;
        }

        bool mentionsRejectedParse =
            normalizedResponse.Contains("reported an invalid", StringComparison.Ordinal) &&
            normalizedResponse.Contains("parse", StringComparison.Ordinal);
        if (mentionsRejectedParse)
        {
            return true;
        }

        bool mentionsUnchangedDocument =
            normalizedResponse.Contains("active document is still unchanged", StringComparison.Ordinal) ||
            normalizedResponse.Contains("document is still unchanged", StringComparison.Ordinal) ||
            normalizedResponse.Contains("remains unchanged", StringComparison.Ordinal) ||
            normalizedResponse.Contains("still unchanged", StringComparison.Ordinal);
        bool mentionsRetry =
            normalizedResponse.Contains("retry", StringComparison.Ordinal) ||
            normalizedResponse.Contains("try again", StringComparison.Ordinal);
        return mentionsUnchangedDocument && mentionsRetry;
    }

    private static bool LooksLikeIncompleteEditResponse(string responseText)
    {
        string normalizedResponse = NormalizeSearchText(responseText);
        if (string.IsNullOrWhiteSpace(normalizedResponse))
        {
            return false;
        }

        if (LooksLikeRejectedEditResponse(normalizedResponse))
        {
            return true;
        }

        if (normalizedResponse.Contains("currently does not run", StringComparison.Ordinal) ||
            normalizedResponse.Contains("does not run in the editor", StringComparison.Ordinal) ||
            normalizedResponse.Contains("still needs a small fix", StringComparison.Ordinal) ||
            normalizedResponse.Contains("still needs a fix", StringComparison.Ordinal) ||
            normalizedResponse.Contains("needs a small fix", StringComparison.Ordinal) ||
            normalizedResponse.Contains("script still needs", StringComparison.Ordinal) ||
            normalizedResponse.Contains("handle the actual returned json shape", StringComparison.Ordinal) ||
            normalizedResponse.Contains("different from { data:", StringComparison.Ordinal) ||
            normalizedResponse.Contains("does not contain a definition for 'data'", StringComparison.Ordinal) ||
            normalizedResponse.Contains("keep repairing autonomously", StringComparison.Ordinal) ||
            normalizedResponse.Contains("the next step is", StringComparison.Ordinal) ||
            normalizedResponse.Contains("before scaling", StringComparison.Ordinal) ||
            normalizedResponse.Contains("if you allow one action", StringComparison.Ordinal) ||
            normalizedResponse.Contains("safe version", StringComparison.Ordinal) ||
            normalizedResponse.Contains("too brittle", StringComparison.Ordinal) ||
            normalizedResponse.Contains("replace the entire active document", StringComparison.Ordinal))
        {
            return true;
        }

        bool offersFollowUpRepair =
            normalizedResponse.Contains("if you want, i can", StringComparison.Ordinal) ||
            normalizedResponse.Contains("i can fix it next", StringComparison.Ordinal) ||
            normalizedResponse.Contains("i can fix this next", StringComparison.Ordinal) ||
            normalizedResponse.Contains("i can adjust it next", StringComparison.Ordinal) ||
            normalizedResponse.Contains("i can retry", StringComparison.Ordinal) ||
            normalizedResponse.Contains("if you want me to keep repairing autonomously", StringComparison.Ordinal);
        bool mentionsFurtherRepairWork =
            normalizedResponse.Contains("fix it next", StringComparison.Ordinal) ||
            normalizedResponse.Contains("adjusting", StringComparison.Ordinal) ||
            normalizedResponse.Contains("safe probe", StringComparison.Ordinal) ||
            normalizedResponse.Contains("response json structure", StringComparison.Ordinal) ||
            normalizedResponse.Contains("actual returned json shape", StringComparison.Ordinal) ||
            normalizedResponse.Contains("small fix", StringComparison.Ordinal) ||
            normalizedResponse.Contains("the next step is", StringComparison.Ordinal) ||
            normalizedResponse.Contains("before scaling", StringComparison.Ordinal) ||
            normalizedResponse.Contains("currently rejecting the script", StringComparison.Ordinal) ||
            normalizedResponse.Contains("safe version", StringComparison.Ordinal);
        return offersFollowUpRepair && mentionsFurtherRepairWork;
    }

    private static bool LooksLikeForcedReplacementNeededResponse(string responseText)
    {
        string normalizedResponse = NormalizeSearchText(responseText);
        return !string.IsNullOrWhiteSpace(normalizedResponse) &&
               ContainsAnyPhrase(normalizedResponse, ForcedReplacementResponsePhrases);
    }

    private static bool LooksLikeMultipleChoiceResponse(string normalizedResponse)
    {
        bool hasFirstChoice = normalizedResponse.Contains("1)", StringComparison.Ordinal) ||
            normalizedResponse.Contains("1.", StringComparison.Ordinal);
        bool hasSecondChoice = normalizedResponse.Contains("2)", StringComparison.Ordinal) ||
            normalizedResponse.Contains("2.", StringComparison.Ordinal);
        return hasFirstChoice &&
               hasSecondChoice &&
               (normalizedResponse.Contains("reply", StringComparison.Ordinal) ||
                normalizedResponse.Contains("choose", StringComparison.Ordinal) ||
                normalizedResponse.Contains("which", StringComparison.Ordinal));
    }

    private static bool ContainsAnyPhrase(string sourceText, IReadOnlyList<string> phrases)
    {
        foreach (string phrase in phrases)
        {
            if (sourceText.Contains(phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSearchText(string? value)
    {
        return NormalizeLineEndings(value)
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u2264', '<')
            .Replace('\u2265', '>')
            .Trim()
            .ToLowerInvariant();
    }

    private static string RemoveConversationBlocks(string sourceText)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        List<string> lines = document.Lines
            .Where(static line => line.Kind is not AiInlineConversationLineKind.Prompt
                and not AiInlineConversationLineKind.Response
                and not AiInlineConversationLineKind.StaleResponse)
            .Select(static line => line.Text)
            .ToList();
        return JoinLines(lines, document.LineEnding);
    }

    private static string KeepOnlyPromptBlock(string sourceText, int promptLineNumber)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        AiInlineConversationPrompt? keptPrompt = document.FindLatestPrompt(promptLineNumber);
        if (keptPrompt is null)
        {
            return RemoveConversationBlocks(sourceText);
        }

        HashSet<int> keptLineNumbers = new(
        [
            .. keptPrompt.PromptLines.Select(static line => line.LineNumber),
            .. keptPrompt.BlockLines.Select(static line => line.LineNumber)
        ]);
        List<string> lines = document.Lines
            .Where(line =>
                (line.Kind is not AiInlineConversationLineKind.Prompt
                and not AiInlineConversationLineKind.Response
                and not AiInlineConversationLineKind.StaleResponse)
                || keptLineNumbers.Contains(line.LineNumber))
            .Select(static line => line.Text)
            .ToList();
        return JoinLines(lines, document.LineEnding);
    }

    private static ConversationUpdate InsertFreshPromptAfterConversation(string sourceText, int promptLineNumber, AiInlineConversationUpdateKind kind)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        List<string> lines = document.Lines.Select(static line => line.Text).ToList();
        AiInlineConversationPrompt? prompt = FindPrompt(document, promptLineNumber);
        if (prompt is null)
        {
            int lastResponseIndex = FindLastResponseIndex(document);
            if (lastResponseIndex >= 0)
            {
                return InsertFreshPromptAtIndex(document, lines, lastResponseIndex + 1, kind);
            }

            return InsertFreshPromptNearLine(sourceText, promptLineNumber, kind);
        }

        int promptIndex = FindPromptIndex(document, prompt.LineNumber);
        int renderedPromptLineCount = GetRenderedPromptLineCount(prompt);
        if (renderedPromptLineCount == 0)
        {
            renderedPromptLineCount = prompt.PromptLines.Count;
        }

        int insertIndex = promptIndex + renderedPromptLineCount;
        while (insertIndex < document.Lines.Count &&
               document.Lines[insertIndex].Kind is AiInlineConversationLineKind.Response or AiInlineConversationLineKind.StaleResponse)
        {
            insertIndex++;
        }

        return InsertFreshPromptAtIndex(document, lines, insertIndex, kind);
    }

    private static ConversationUpdate InsertFreshPromptNearOriginalConversation(
        string cleanedSource,
        string originalSource,
        int originalPromptLineNumber,
        AiInlineConversationUpdateKind kind)
    {
        AiInlineConversationDocument originalDocument = AiInlineConversationParser.Parse(originalSource);
        int keptLineCountBeforePrompt = originalDocument.Lines
            .Take(Math.Max(0, originalPromptLineNumber - 1))
            .Count(static line => line.Kind is not AiInlineConversationLineKind.Prompt
                and not AiInlineConversationLineKind.Response
                and not AiInlineConversationLineKind.StaleResponse);

        AiInlineConversationDocument cleanedDocument = AiInlineConversationParser.Parse(cleanedSource);
        List<string> lines = cleanedDocument.Lines.Select(static line => line.Text).ToList();
        int insertIndex = Math.Clamp(keptLineCountBeforePrompt, 0, lines.Count);

        return InsertFreshPromptAtIndex(cleanedDocument, lines, insertIndex, kind);
    }

    private static ConversationUpdate InsertFreshPromptAtIndex(
        AiInlineConversationDocument document,
        List<string> lines,
        int insertIndex,
        AiInlineConversationUpdateKind kind)
    {
        insertIndex = Math.Clamp(insertIndex, 0, lines.Count);

        bool needsLeadingSpacer = insertIndex > 0 && !string.IsNullOrWhiteSpace(lines[insertIndex - 1]);
        if (needsLeadingSpacer)
        {
            lines.Insert(insertIndex, string.Empty);
            insertIndex++;
        }

        lines.Insert(insertIndex, "## ");

        bool needsTrailingSpacer = insertIndex + 1 < lines.Count && !string.IsNullOrWhiteSpace(lines[insertIndex + 1]);
        if (needsTrailingSpacer)
        {
            lines.Insert(insertIndex + 1, string.Empty);
        }

        return new(JoinLines(lines, document.LineEnding), kind, insertIndex + 1, 4);
    }

    private static ConversationUpdate InsertFreshPromptNearLine(string sourceText, int targetLineNumber, AiInlineConversationUpdateKind kind)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        List<string> lines = document.Lines.Select(static line => line.Text).ToList();
        int insertIndex = Math.Clamp(targetLineNumber - 1, 0, lines.Count);
        return InsertFreshPromptAtIndex(document, lines, insertIndex, kind);
    }

    private static string JoinLines(IReadOnlyList<string> lines, string lineEnding)
    {
        return string.Join(lineEnding, lines);
    }

    private static int FindPromptIndex(AiInlineConversationDocument document, int promptLineNumber)
    {
        if (promptLineNumber < 1 || promptLineNumber > document.Lines.Count)
        {
            return -1;
        }

        for (int index = 0; index < document.Lines.Count; index++)
        {
            if (document.Lines[index].LineNumber == promptLineNumber &&
                document.Lines[index].Kind == AiInlineConversationLineKind.Prompt)
            {
                return index;
            }
        }

        return -1;
    }

    private static AiInlineConversationPrompt? FindPrompt(AiInlineConversationDocument document, int promptLineNumber)
    {
        if (promptLineNumber < 1 || document.Prompts.Count == 0)
        {
            return null;
        }

        return document.FindLatestPrompt(promptLineNumber);
    }

    private static int FindLastResponseIndex(AiInlineConversationDocument document)
    {
        for (int index = document.Lines.Count - 1; index >= 0; index--)
        {
            if (document.Lines[index].Kind is AiInlineConversationLineKind.Response or AiInlineConversationLineKind.StaleResponse)
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeLineEndings(string? value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string NormalizePromptCommandText(string? value)
    {
        string[] segments = (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", segments);
    }

    private static PreparedPrompt PreparePromptForExecution(AiInlineConversationRequest request, string promptText)
    {
        string rawPrompt = NormalizeLineEndings(promptText).Trim();
        if (!ShouldCompactPrompt(rawPrompt))
        {
            return new(rawPrompt, rawPrompt, WasCompacted: false);
        }

        string compactedPrompt = BuildCompactedPrompt(request, rawPrompt);
        if (string.IsNullOrWhiteSpace(compactedPrompt))
        {
            compactedPrompt = rawPrompt;
        }

        return new(
            rawPrompt,
            compactedPrompt,
            WasCompacted: !string.Equals(rawPrompt, compactedPrompt, StringComparison.Ordinal));
    }

    private static bool ShouldCompactPrompt(string promptText)
    {
        if (string.IsNullOrWhiteSpace(promptText))
        {
            return false;
        }

        string normalized = NormalizeSearchText(promptText);
        int docMarkerCount = PromptDocMarkerPhrases.Count(
            marker => normalized.Contains(marker, StringComparison.Ordinal));
        int methodCount = ExtractApiOperations(promptText)
            .Select(static operation => operation.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        int urlCount = PromptUrlRegex.Matches(promptText).Count;

        return (docMarkerCount >= 2 && (methodCount >= 3 || urlCount >= 3)) ||
               (promptText.Length >= PromptCompactionMinLength && docMarkerCount >= 1 && urlCount >= 2);
    }

    private static string BuildCompactedPrompt(AiInlineConversationRequest request, string promptText)
    {
        List<string> operations = ExtractApiOperations(promptText);
        string normalizedPrompt = NormalizeSearchText(promptText);
        string? currentTarget = TryExtractConfiguredUrl(request.SourceText);

        List<string> requirements = [];
        AddRequirement(
            requirements,
            normalizedPrompt.Contains("fully test", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("api surface", StringComparison.Ordinal),
            "Fully test the target API surface from one coherent request script.");
        AddRequirement(
            requirements,
            normalizedPrompt.Contains("stash", StringComparison.Ordinal),
            "Use stash rows to capture the important fields from each step.");
        AddRequirement(
            requirements,
            normalizedPrompt.Contains("dynamic", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("guid()", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("trace_id", StringComparison.Ordinal),
            "Prefer dynamically captured values over hard-coded follow-up ids when possible.");
        AddRequirement(
            requirements,
            normalizedPrompt.Contains("log", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("trace", StringComparison.Ordinal),
            "Add concise logs so the execution trace shows each step that ran.");
        AddRequirement(
            requirements,
            normalizedPrompt.Contains("no need to ask questions", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("don't ask", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("dont ask", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("do not ask", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("just implement", StringComparison.Ordinal),
            "Do not ask follow-up questions unless a real product decision is missing.");
        AddRequirement(
            requirements,
            ContainsRepeatedIdQueryRequirement(normalizedPrompt),
            "Include filtered list coverage using repeated `id` query values on the collection endpoint.");
        AddRequirement(
            requirements,
            HasMethod(operations, "POST") && HasMethod(operations, "DELETE"),
            "Create an object, capture the returned id, and reuse it for the follow-up GET, PUT, PATCH, and DELETE steps.");
        AddRequirement(
            requirements,
            HasMethod(operations, "POST") || HasMethod(operations, "PUT") || HasMethod(operations, "PATCH"),
            "Use JSON request bodies and `request.content_type` for the write steps.");
        AddRequirement(
            requirements,
            !string.IsNullOrWhiteSpace(currentTarget) &&
            operations.Any(operation => !operation.Contains(currentTarget, StringComparison.OrdinalIgnoreCase)),
            $"Replace the current target `{currentTarget}` with the API surface above; do not leave the previous endpoint half-intact.");
        AddRequirement(
            requirements,
            condition: true,
            "Use documented ForRest request mutation patterns: `request.method`, `request.url`, `request.body`, `request.content_type`, `request.send()`, `stash`, and top-level `expect`.");
        AddRequirement(
            requirements,
            condition: true,
            "Leave only one coherent runnable ForRest request in the final document. Do not copy the pasted API docs.");

        StringBuilder builder = new();
        builder.AppendLine("Rewrite the active request in place. Output runnable ForRest source only.");
        builder.AppendLine("Use the pasted API reference as requirements, not as content to copy.");

        if (operations.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("API operations to cover:");
            foreach (string operation in operations)
            {
                builder.AppendLine($"- {operation}");
            }
        }

        if (requirements.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Implementation requirements:");
            foreach (string requirement in requirements)
            {
                builder.AppendLine($"- {requirement}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static List<string> ExtractApiOperations(string promptText)
    {
        List<string> operations = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string? pendingMethod = null;

        foreach (string rawLine in NormalizeLineEndings(promptText).Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            Match methodUrlMatch = PromptMethodUrlRegex.Match(line);
            if (methodUrlMatch.Success)
            {
                AddOperation(
                    operations,
                    seen,
                    $"{methodUrlMatch.Groups["method"].Value.ToUpperInvariant()} {TrimExtractedUrl(methodUrlMatch.Groups["url"].Value)}");
                pendingMethod = null;
                continue;
            }

            if (PromptHttpMethods.Contains(line))
            {
                pendingMethod = line.ToUpperInvariant();
                continue;
            }

            if (pendingMethod is not null && TryExtractUrl(line, out string extractedUrl))
            {
                AddOperation(operations, seen, $"{pendingMethod} {extractedUrl}");
                pendingMethod = null;
                continue;
            }

            pendingMethod = null;
        }

        return operations;
    }

    private static void AddOperation(List<string> operations, HashSet<string> seen, string operation)
    {
        if (seen.Add(operation))
        {
            operations.Add(operation);
        }
    }

    private static bool HasMethod(IEnumerable<string> operations, string method)
    {
        return operations.Any(operation => operation.StartsWith($"{method} ", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryExtractUrl(string line, out string url)
    {
        Match urlMatch = PromptUrlRegex.Match(line);
        if (!urlMatch.Success)
        {
            url = string.Empty;
            return false;
        }

        url = TrimExtractedUrl(urlMatch.Value);
        return true;
    }

    private static string TrimExtractedUrl(string value)
    {
        return value.Trim().TrimEnd('.', ',', ';', ')', ']', '"', '\'');
    }

    private static bool ContainsRepeatedIdQueryRequirement(string normalizedPrompt)
    {
        return normalizedPrompt.Contains("id string[] query", StringComparison.Ordinal) ||
               normalizedPrompt.Contains("?id=3&id=5&id=10", StringComparison.Ordinal) ||
               normalizedPrompt.Contains("supports multiple values by repeating the parameter", StringComparison.Ordinal) ||
               normalizedPrompt.Contains("repeating the parameter in the query string", StringComparison.Ordinal);
    }

    private static string? TryExtractConfiguredUrl(string sourceText)
    {
        Match match = RequestUrlDirectiveRegex.Match(NormalizeLineEndings(sourceText));
        return match.Success
            ? match.Groups["url"].Value.Trim()
            : null;
    }

    private static void AddRequirement(List<string> requirements, bool condition, string requirement)
    {
        if (!condition || string.IsNullOrWhiteSpace(requirement))
        {
            return;
        }

        if (!requirements.Contains(requirement, StringComparer.Ordinal))
        {
            requirements.Add(requirement.Trim());
        }
    }

    private static string BuildObjective(AiInlineConversationRequest request)
    {
        return $"Update the active ForRest request document '{request.DocumentTitle}'. Use local docs and active document tools before guessing. Prefer zero clarification turns when the request is actionable; choose reasonable defaults and edit the current request in place. Treat pasted API docs or prose as requirements only and leave the final document as runnable ForRest source with one coherent request target. For API-surface rewrites, use documented request mutation, stash, and top-level `expect` patterns. Reply briefly after the document has been updated.";
    }

    private static string BuildAutonomousRepairPrompt(string originalPrompt, string latestResponseText, int attemptNumber)
    {
        bool forceFullReplace = attemptNumber > 1 || LooksLikeForcedReplacementNeededResponse(latestResponseText);
        string normalizedLatestResponse = NormalizeLineEndings(latestResponseText).Trim();
        string latestFailureSection = string.IsNullOrWhiteSpace(normalizedLatestResponse)
            ? string.Empty
            : $"Latest failed reply (do not repeat it back to the user):{Environment.NewLine}{normalizedLatestResponse}{Environment.NewLine}{Environment.NewLine}";
        string replacementDirective = forceFullReplace
            ? "Your next document mutation must be a full `replace_active_document` call with the final working request." + Environment.NewLine +
              "Do not propose a partial logging probe, a 'safe' intermediate version, or an extra permission step." + Environment.NewLine +
              "Do not tell the user the document is too brittle to patch; replace it now with the corrected request." + Environment.NewLine
            : string.Empty;

        return
            $"""
            Your previous turn did not leave the active ForRest request updated.
            Treat this as an autonomous repair pass.
            Do not ask the user for clarification unless a real product decision is still missing.
            Read the active document again, inspect the latest diagnostics, use local docs if needed, and apply a valid edit now.
            If the user asked to iterate, enumerate, batch, or stash values, transform the current request in place instead of asking whether to replace it or create another request.
            Phrases like `3 times`, `repeat N times`, or `at least N times` are loop requests. Prefer documented `foreach`, range, and `max_send_iterations` flow over manually duplicating similar request blocks.
            If the user asked to test an API surface or multiple methods, use the documented `request-send`, `request-method`, `request-url`, `request-headers`, `request-body`, `request-content-type`, `api-surface-crud`, `stash`, and top-level `expect` patterns.
            If the user pasted API docs or prose into chat, strip that prose from the final document and leave only runnable ForRest source.
            If the current request still points at the old endpoint, replace that target instead of leaving the previous URL or method in place.
            If a requested field name is slightly wrong but the closest valid field is obvious, choose the closest valid field and note the assumption after the edit.
            ForRest syntax guardrails: top-level request config uses bare directives like `method`, `url`, `header`, and `content_type`; dotted members like `request.method`, `request.url`, `request.body`, `request.content_type`, and `request.headers[...]` belong inside flow code before `request.send()`.
            If the user needs randomized or unique values, use only documented ForRest helpers from local docs such as `guid()` runtime values and documented `strings`, `convert`, or `time` helpers. Do not invent `Math.*`, instance methods like `.Substring(...)`, or arbitrary C# APIs.
            Prefer `response.someField` or `response["Some Field"]` for JSON object members.
            When the response body root is an array, iterate `response` directly or use `response[index]`.
            `response.json()` returns a raw JsonNode; use it only with explicit indexers or `AsArray()`, not dot-member access.
            {replacementDirective}If a full rewrite is rejected, reduce the change, fix the syntax, and retry with a valid document.
            If a partial edit leaves the request still broken or still needing one more fix, keep repairing it now instead of telling the user what you would fix next.
            Prefer a smaller valid improvement over an explanatory refusal or plan.
            After the document has been updated, reply briefly with what you changed.

            {latestFailureSection}Original user request: {originalPrompt}
            """;
    }

    private static string BuildDebugText(
        AiInlineConversationRequest request,
        AiInlineConversationPrompt prompt,
        PreparedPrompt preparedPrompt,
        DeterministicCrudCandidate? deterministicCrudCandidate,
        string latestSource,
        AiTurnExecutionResult turn)
    {
        List<string> lines =
        [
            $"AI prompt line: {prompt.LineNumber}",
            $"Prompt: {prompt.PromptText}",
            $"Prompt compacted: {preparedPrompt.WasCompacted}",
            $"Succeeded: {turn.Succeeded}",
            $"Session reset: {turn.SessionReset}",
            $"Executor autonomous repair attempts: {turn.AutonomousEditRecoveryAttempts}",
        ];

        if (deterministicCrudCandidate is not null)
        {
            lines.Add($"Deterministic rewrite candidate line: {deterministicCrudCandidate.Prompt.LineNumber}");
            if (!ArePromptsEquivalent(prompt, deterministicCrudCandidate.Prompt))
            {
                lines.Add($"Deterministic candidate prompt: {deterministicCrudCandidate.Prompt.PromptText}");
            }

            if (!ArePreparedPromptsEquivalent(preparedPrompt, deterministicCrudCandidate.PreparedPrompt))
            {
                lines.Add($"Deterministic candidate compacted: {deterministicCrudCandidate.PreparedPrompt.WasCompacted}");
                if (deterministicCrudCandidate.PreparedPrompt.WasCompacted)
                {
                    lines.Add($"Deterministic candidate effective prompt: {deterministicCrudCandidate.PreparedPrompt.EffectivePrompt}");
                }
            }
        }

        if (turn.DeterministicFallbackApplied)
        {
            lines.Add("Deterministic rewrite: True");
        }

        if (preparedPrompt.WasCompacted)
        {
            lines.Add($"Effective prompt: {preparedPrompt.EffectivePrompt}");
        }

        if (turn.DeterministicFallbackApplied || !turn.Succeeded || preparedPrompt.WasCompacted || deterministicCrudCandidate is not null)
        {
            string attemptedSource = RemoveConversationBlocks(request.SourceText);
            if (!string.IsNullOrWhiteSpace(attemptedSource))
            {
                lines.Add("Attempted request source:");
                lines.Add(attemptedSource);
            }
        }

        if (HasDocumentChanged(request.SourceText, latestSource))
        {
            string latestRequestSource = RemoveConversationBlocks(latestSource);
            if (!string.IsNullOrWhiteSpace(latestRequestSource))
            {
                lines.Add("Latest request source:");
                lines.Add(latestRequestSource);
            }
        }

        if (turn.Issues.Count > 0)
        {
            lines.Add("Issues:");
            lines.AddRange(turn.Issues.Select(static issue => $"- [{issue.Severity}] {issue.Message}"));
        }

        if (!string.IsNullOrWhiteSpace(turn.DebugTrace))
        {
            lines.Add("AI execution trace:");
            lines.Add(turn.DebugTrace);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildStatusText(AiTurnExecutionResult turn)
    {
        if (turn.DeterministicFallbackApplied)
        {
            return "Applied built-in request rewrite.";
        }

        if (turn.Succeeded)
        {
            return "AI replied.";
        }

        if (turn.ResponseText.StartsWith("AI request timed out", StringComparison.OrdinalIgnoreCase))
        {
            return "AI request timed out.";
        }

        if (turn.ResponseText.StartsWith("AI request canceled", StringComparison.OrdinalIgnoreCase))
        {
            return "AI request canceled.";
        }

        return "AI could not complete the request.";
    }

    private static string BuildCommandDebugText(AiInlineConversationPrompt prompt, string commandName)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"AI prompt line: {prompt.LineNumber}",
                $"Prompt: {prompt.PromptText}",
                $"Command: {commandName}",
                "Succeeded: True",
                "Session reset: False",
            ]);
    }

    private static string BuildPromptCommandHelpText()
    {
        return string.Join(
            "\n",
            [
                "Supported prompt commands:",
                "- `reset` clears inline AI prompt and response history and reopens a fresh prompt.",
                "- `clear responses` removes inline AI replies and keeps the prompt lines.",
                "- `collapse` keeps only the latest inline AI exchange and reopens a fresh prompt.",
                "- `help` shows this command list.",
                "- `commands` is an alias for `help`.",
            ]);
    }

    private static ConversationUpdate ClearResponsesAndReopenPrompt(string sourceText, AiInlineConversationPrompt commandPrompt)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        HashSet<int> removedPromptLineNumbers = new(commandPrompt.PromptLines.Select(static line => line.LineNumber));
        List<string> lines = document.Lines
            .Where(line =>
                (line.Kind is not AiInlineConversationLineKind.Response
                and not AiInlineConversationLineKind.StaleResponse) &&
                !removedPromptLineNumbers.Contains(line.LineNumber))
            .Select(static line => line.Text)
            .ToList();

        int insertIndex = document.Lines
            .Take(Math.Max(0, commandPrompt.LineNumber - 1))
            .Count(static line => line.Kind is not AiInlineConversationLineKind.Response
                and not AiInlineConversationLineKind.StaleResponse);

        return InsertFreshPromptAtIndex(document, lines, insertIndex, AiInlineConversationUpdateKind.ResponseOnly);
    }

    private static ConversationUpdate CollapseConversationHistory(string sourceText, AiInlineConversationPrompt commandPrompt)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        AiInlineConversationPrompt? keptPrompt = document.Prompts
            .Where(prompt => prompt.LineNumber < commandPrompt.LineNumber)
            .LastOrDefault();
        if (keptPrompt is null)
        {
            string cleanedSource = RemoveConversationBlocks(sourceText);
            return InsertFreshPromptNearOriginalConversation(
                cleanedSource,
                sourceText,
                commandPrompt.LineNumber,
                AiInlineConversationUpdateKind.ResponseOnly);
        }

        HashSet<int> keptConversationLineNumbers = new(
        [
            .. keptPrompt.PromptLines.Select(static line => line.LineNumber),
            .. keptPrompt.BlockLines
                .Where(static line => line.Kind is AiInlineConversationLineKind.Response or AiInlineConversationLineKind.StaleResponse)
                .Select(static line => line.LineNumber)
        ]);
        List<string> lines = document.Lines
            .Where(line =>
                (line.Kind is not AiInlineConversationLineKind.Prompt
                and not AiInlineConversationLineKind.Response
                and not AiInlineConversationLineKind.StaleResponse)
                || keptConversationLineNumbers.Contains(line.LineNumber))
            .Select(static line => line.Text)
            .ToList();
        string collapsedSource = JoinLines(lines, document.LineEnding);

        return InsertFreshPromptAfterConversation(
            collapsedSource,
            keptPrompt.LineNumber,
            AiInlineConversationUpdateKind.ResponseOnly);
    }

    private static int GetRenderedPromptLineCount(AiInlineConversationPrompt prompt)
    {
        return prompt.GetRenderedPromptLines().Count;
    }

    private sealed record PreparedPrompt(
        string RawPrompt,
        string EffectivePrompt,
        bool WasCompacted);

    private sealed record DeterministicCrudCandidate(
        AiInlineConversationPrompt Prompt,
        PreparedPrompt PreparedPrompt);

    private readonly record struct ConversationUpdate(
        string Text,
        AiInlineConversationUpdateKind Kind,
        int SuggestedCursorLineNumber,
        int SuggestedCursorColumn);

    private static bool ArePreparedPromptsEquivalent(PreparedPrompt left, PreparedPrompt right)
    {
        return string.Equals(left.RawPrompt, right.RawPrompt, StringComparison.Ordinal) &&
               string.Equals(left.EffectivePrompt, right.EffectivePrompt, StringComparison.Ordinal) &&
               left.WasCompacted == right.WasCompacted;
    }

    private static bool ArePromptsEquivalent(AiInlineConversationPrompt left, AiInlineConversationPrompt right)
    {
        return left.LineNumber == right.LineNumber &&
               string.Equals(left.PromptText, right.PromptText, StringComparison.Ordinal);
    }
}
