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
    int? SuggestedCursorLineNumber = null,
    int SuggestedCursorColumn = 4)
{
    public static AiInlineConversationResult NotHandled(string sourceText)
    {
        return new(false, true, sourceText ?? string.Empty, string.Empty, string.Empty);
    }
}

public interface IAiInlineConversationService
{
    Task<AiInlineConversationResult> TryHandleAsync(AiInlineConversationRequest request, CancellationToken cancellationToken = default);
}

public sealed class AiInlineConversationService : IAiInlineConversationService
{
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
        AiInlineConversationPrompt? prompt = ResolvePrompt(document, request.CursorLineNumber);
        if (prompt is null)
        {
            return AiInlineConversationResult.NotHandled(request.SourceText);
        }

        AiTurnExecutionResult turn = await _turnExecutor.ExecuteAsync(
            new(
                ConversationId: request.DocumentId,
                Objective: BuildObjective(request),
                Prompt: prompt.PromptText,
                Settings: request.Settings,
                ActiveDocumentHost: request.ActiveDocumentHost),
            cancellationToken);

        string latestSource = request.ActiveDocumentHost.GetActiveDocument()?.SourceText ?? request.SourceText;
        ConversationUpdate updatedDocument = BuildUpdatedDocument(request.SourceText, latestSource, prompt, turn);

        return new(
            Handled: true,
            Succeeded: turn.Succeeded,
            UpdatedText: updatedDocument.Text,
            StatusText: turn.Succeeded ? "AI replied." : "AI could not complete the request.",
            DebugText: BuildDebugText(prompt, turn),
            SuggestedCursorLineNumber: updatedDocument.SuggestedCursorLineNumber,
            SuggestedCursorColumn: updatedDocument.SuggestedCursorColumn);
    }

    private static AiInlineConversationPrompt? ResolvePrompt(AiInlineConversationDocument document, int cursorLineNumber)
    {
        if (document.Prompts.Count == 0)
        {
            return null;
        }

        if (cursorLineNumber >= 1 && cursorLineNumber <= document.Lines.Count)
        {
            AiInlineConversationLine cursorLine = document.Lines[cursorLineNumber - 1];
            if (cursorLine.IsConversationLine)
            {
                AiInlineConversationPrompt? prompt = document.FindLatestPrompt(cursorLineNumber);
                return IsActionablePrompt(prompt) ? prompt : null;
            }
        }

        AiInlineConversationLine? lastContentLine = document.Lines.LastOrDefault(
            static line => line.Kind is not AiInlineConversationLineKind.Blank);
        if (lastContentLine?.Kind == AiInlineConversationLineKind.Prompt)
        {
            AiInlineConversationPrompt? prompt = document.FindLatestPrompt(lastContentLine.LineNumber);
            return IsActionablePrompt(prompt) ? prompt : null;
        }

        return null;
    }

    private static bool IsActionablePrompt(AiInlineConversationPrompt? prompt)
    {
        return prompt is not null && !string.IsNullOrWhiteSpace(prompt.PromptText);
    }

    private static ConversationUpdate BuildUpdatedDocument(
        string originalSource,
        string latestSource,
        AiInlineConversationPrompt originalPrompt,
        AiTurnExecutionResult turn)
    {
        if (turn.Succeeded && HasDocumentChanged(originalSource, latestSource))
        {
            string cleaned = RemoveConversationBlocks(latestSource);
            return InsertFreshPromptNearLine(cleaned, originalPrompt.LineNumber);
        }

        int promptLineNumber = ResolvePromptLineNumber(latestSource, originalPrompt);
        string withResponse = AiInlineConversationFormatter.ApplyResponse(latestSource, promptLineNumber, turn.ResponseText);
        string latestOnly = KeepOnlyPromptBlock(withResponse, promptLineNumber);
        return InsertFreshPromptAfterConversation(latestOnly, promptLineNumber);
    }

    private static int ResolvePromptLineNumber(string sourceText, AiInlineConversationPrompt originalPrompt)
    {
        AiInlineConversationDocument current = AiInlineConversationParser.Parse(sourceText);
        if (current.Prompts.Count == 0)
        {
            return originalPrompt.LineNumber;
        }

        AiInlineConversationPrompt? match = current.Prompts
            .Where(prompt => string.Equals(prompt.PromptText, originalPrompt.PromptText, StringComparison.Ordinal))
            .OrderBy(prompt => Math.Abs(prompt.LineNumber - originalPrompt.LineNumber))
            .FirstOrDefault();
        return match?.LineNumber ?? current.FindLatestPrompt(originalPrompt.LineNumber)?.LineNumber ?? originalPrompt.LineNumber;
    }

    private static bool HasDocumentChanged(string originalSource, string latestSource)
    {
        return !string.Equals(
            NormalizeLineEndings(originalSource).Trim(),
            NormalizeLineEndings(latestSource).Trim(),
            StringComparison.Ordinal);
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

    private static ConversationUpdate InsertFreshPromptAfterConversation(string sourceText, int promptLineNumber)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        List<string> lines = document.Lines.Select(static line => line.Text).ToList();
        int promptIndex = FindPromptIndex(document, promptLineNumber);
        if (promptIndex < 0)
        {
            return InsertFreshPromptNearLine(sourceText, document.Lines.Count + 1);
        }

        int insertIndex = promptIndex + 1;
        while (insertIndex < document.Lines.Count &&
               document.Lines[insertIndex].Kind is AiInlineConversationLineKind.Response or AiInlineConversationLineKind.StaleResponse)
        {
            insertIndex++;
        }

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

        return new(JoinLines(lines, document.LineEnding), insertIndex + 1, 4);
    }

    private static ConversationUpdate InsertFreshPromptNearLine(string sourceText, int targetLineNumber)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        List<string> lines = document.Lines.Select(static line => line.Text).ToList();
        int insertIndex = Math.Clamp(targetLineNumber - 1, 0, lines.Count);

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

        return new(JoinLines(lines, document.LineEnding), insertIndex + 1, 4);
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

    private static string NormalizeLineEndings(string? value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string BuildObjective(AiInlineConversationRequest request)
    {
        return $"Help with the active ForRest request document '{request.DocumentTitle}'. Use the local docs and active document tools before guessing.";
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

    private readonly record struct ConversationUpdate(string Text, int SuggestedCursorLineNumber, int SuggestedCursorColumn);
}
