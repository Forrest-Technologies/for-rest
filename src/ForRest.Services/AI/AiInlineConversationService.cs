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
    string DebugText)
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
        int promptLineNumber = ResolvePromptLineNumber(latestSource, prompt);
        string updatedText = AiInlineConversationFormatter.ApplyResponse(latestSource, promptLineNumber, turn.ResponseText);
        updatedText = FadeOlderResponses(updatedText, request.Settings.Conversation.MaxHistoryTurns);

        return new(
            Handled: true,
            Succeeded: turn.Succeeded,
            UpdatedText: updatedText,
            StatusText: turn.Succeeded ? "AI replied." : "AI could not complete the request.",
            DebugText: BuildDebugText(prompt, turn));
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
                return document.FindLatestPrompt(cursorLineNumber);
            }
        }

        AiInlineConversationLine? lastContentLine = document.Lines.LastOrDefault(
            static line => line.Kind is not AiInlineConversationLineKind.Blank);
        if (lastContentLine?.Kind == AiInlineConversationLineKind.Prompt)
        {
            return document.FindLatestPrompt(lastContentLine.LineNumber);
        }

        return null;
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

    private static string FadeOlderResponses(string sourceText, int maxHistoryTurns)
    {
        int maxTurns = Math.Max(1, maxHistoryTurns);
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        List<AiInlineConversationPrompt> activePrompts = document.Prompts
            .Where(static prompt => prompt.HasActiveResponse)
            .ToList();
        if (activePrompts.Count <= maxTurns)
        {
            return sourceText;
        }

        string updated = sourceText;
        foreach (AiInlineConversationPrompt stalePrompt in activePrompts.Take(activePrompts.Count - maxTurns))
        {
            updated = AiInlineConversationFormatter.FadePromptResponses(updated, stalePrompt.LineNumber);
        }

        return updated;
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
}
