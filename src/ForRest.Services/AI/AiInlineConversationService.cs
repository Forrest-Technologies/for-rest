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
            ResponseText: turn.ResponseText,
            PromptLineNumber: prompt.LineNumber,
            UpdateKind: updatedDocument.Kind,
            SuggestedCursorLineNumber: updatedDocument.SuggestedCursorLineNumber,
            SuggestedCursorColumn: updatedDocument.SuggestedCursorColumn);
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
        return $"Help with the active ForRest request document '{request.DocumentTitle}'. Use the local docs and active document tools before guessing. Active document tools expose the request script without inline chat markers. Keep clarification short and, once a reasonable default exists, prefer editing over more back-and-forth. If a targeted patch fails, prefer replacing the full request instead of asking the user for confirmation.";
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

    private readonly record struct ConversationUpdate(
        string Text,
        AiInlineConversationUpdateKind Kind,
        int SuggestedCursorLineNumber,
        int SuggestedCursorColumn);
}
