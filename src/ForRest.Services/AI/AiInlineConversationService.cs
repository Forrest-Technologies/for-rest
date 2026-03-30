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
    private const int MaxAutonomousRepairAttempts = 2;
    private static readonly string[] StuckResponsePhrases =
    [
        "can't apply",
        "cannot apply",
        "can't safely",
        "cannot safely",
        "can't update",
        "cannot update",
        "can't rewrite",
        "cannot rewrite",
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
        if (prompt is null)
        {
            return AiInlineConversationResult.NotHandled(request.SourceText);
        }

        AiInlineConversationResult? commandResult = TryHandlePromptCommand(request.SourceText, prompt);
        if (commandResult is not null)
        {
            return commandResult;
        }

        AiTurnExecutionResult turn = await ExecuteTurnWithAutonomousRecoveryAsync(request, prompt, cancellationToken);

        string latestSource = request.ActiveDocumentHost.GetActiveDocument()?.SourceText ?? request.SourceText;
        ConversationUpdate updatedDocument = BuildUpdatedDocument(request.SourceText, latestSource, prompt, turn);

        return new(
            Handled: true,
            Succeeded: turn.Succeeded,
            UpdatedText: updatedDocument.Text,
            StatusText: turn.Succeeded ? "AI replied." : "AI could not complete the request.",
            DebugText: BuildDebugText(prompt, turn),
            ResponseText: turn.ResponseText,
            PromptLineNumber: prompt.LineNumber,
            UpdateKind: updatedDocument.Kind,
            SuggestedCursorLineNumber: updatedDocument.SuggestedCursorLineNumber,
            SuggestedCursorColumn: updatedDocument.SuggestedCursorColumn);
    }

    private static AiInlineConversationResult? TryHandlePromptCommand(string sourceText, AiInlineConversationPrompt prompt)
    {
        string commandText = prompt.PromptText.Trim();
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
        CancellationToken cancellationToken)
    {
        AiTurnExecutionResult turn = await ExecuteTurnAsync(request, prompt.PromptText, cancellationToken);
        bool sessionReset = turn.SessionReset;

        for (int attempt = 0; attempt < MaxAutonomousRepairAttempts; attempt++)
        {
            string latestSource = request.ActiveDocumentHost.GetActiveDocument()?.SourceText ?? request.SourceText;
            if (!ShouldAttemptAutonomousRepair(prompt.PromptText, request.SourceText, latestSource, turn))
            {
                return turn with { SessionReset = sessionReset };
            }

            AiTurnExecutionResult repairTurn = await ExecuteTurnAsync(
                request,
                BuildAutonomousRepairPrompt(prompt.PromptText),
                cancellationToken);
            sessionReset |= repairTurn.SessionReset;
            turn = repairTurn with { SessionReset = sessionReset };
        }

        string finalSource = request.ActiveDocumentHost.GetActiveDocument()?.SourceText ?? request.SourceText;
        if (ShouldAttemptAutonomousRepair(prompt.PromptText, request.SourceText, finalSource, turn))
        {
            return turn with
            {
                Succeeded = false,
                SessionReset = sessionReset,
            };
        }

        return turn with { SessionReset = sessionReset };
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
        if (HasDocumentChanged(originalSource, latestSource))
        {
            return false;
        }

        if (turn.ResponseText.StartsWith("AI request failed:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (LooksLikeStuckResponse(turn.ResponseText))
        {
            return true;
        }

        return !turn.Succeeded && AiPromptIntentClassifier.IsLikelyEditPrompt(promptText);
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
               LooksLikeMultipleChoiceResponse(normalized);
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

        HashSet<int> keptLineNumbers = new([keptPrompt.LineNumber, .. keptPrompt.BlockLines.Select(static line => line.LineNumber)]);
        List<string> lines = document.Lines
            .Where(line =>
                line.Kind is not AiInlineConversationLineKind.Prompt
                and not AiInlineConversationLineKind.Response
                and not AiInlineConversationLineKind.StaleResponse
                || keptLineNumbers.Contains(line.LineNumber))
            .Select(static line => line.Text)
            .ToList();
        return JoinLines(lines, document.LineEnding);
    }

    private static ConversationUpdate InsertFreshPromptAfterConversation(string sourceText, int promptLineNumber, AiInlineConversationUpdateKind kind)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        List<string> lines = document.Lines.Select(static line => line.Text).ToList();
        int promptIndex = FindPromptIndex(document, promptLineNumber);
        if (promptIndex < 0)
        {
            int lastResponseIndex = FindLastResponseIndex(document);
            if (lastResponseIndex >= 0)
            {
                return InsertFreshPromptAtIndex(document, lines, lastResponseIndex + 1, kind);
            }

            return InsertFreshPromptNearLine(sourceText, promptLineNumber, kind);
        }

        int insertIndex = promptIndex + 1;
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

    private static string BuildObjective(AiInlineConversationRequest request)
    {
        return $"Help with the active ForRest request document '{request.DocumentTitle}'. Use the local docs and active document tools before guessing. Active document tools expose the request script without inline chat markers. Keep clarification short and, once a reasonable default exists, prefer editing over more back-and-forth. If the user asks to iterate, enumerate, batch, or stash values, default to modifying the current request in place. If a requested field name appears misspelled but the closest valid field is obvious, choose the closest valid field and state that assumption after the edit. If a targeted patch fails, prefer replacing the full request instead of asking the user for confirmation. If an edit is rejected or leaves the document unchanged, read the active document again, use the returned diagnostics, and retry internally instead of surfacing the failed attempt.";
    }

    private static string BuildAutonomousRepairPrompt(string originalPrompt)
    {
        return
            $"""
            Your previous turn did not leave the active ForRest request updated.
            Treat this as an autonomous repair pass.
            Do not ask the user for clarification unless a real product decision is still missing.
            Read the active document again, inspect the latest diagnostics, use local docs if needed, and apply a valid edit now.
            If the user asked to iterate, enumerate, batch, or stash values, transform the current request in place instead of asking whether to replace it or create another request.
            If a requested field name is slightly wrong but the closest valid field is obvious, choose the closest valid field and note the assumption after the edit.
            If a full rewrite is rejected, reduce the change, fix the syntax, and retry with a valid document.
            Prefer a smaller valid improvement over an explanatory refusal or plan.
            After the document has been updated, reply briefly with what you changed.

            Original user request: {originalPrompt}
            """;
    }

    private static string BuildDebugText(AiInlineConversationPrompt prompt, AiTurnExecutionResult turn)
    {
        List<string> lines =
        [
            $"AI prompt line: {prompt.LineNumber}",
            $"Prompt: {prompt.PromptText}",
            $"Succeeded: {turn.Succeeded}",
            $"Session reset: {turn.SessionReset}",
        ];

        if (turn.Issues.Count > 0)
        {
            lines.Add("Issues:");
            lines.AddRange(turn.Issues.Select(static issue => $"- [{issue.Severity}] {issue.Message}"));
        }

        return string.Join(Environment.NewLine, lines);
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
                "- `help` shows this command list.",
                "- `commands` is an alias for `help`.",
            ]);
    }

    private readonly record struct ConversationUpdate(
        string Text,
        AiInlineConversationUpdateKind Kind,
        int SuggestedCursorLineNumber,
        int SuggestedCursorColumn);
}
