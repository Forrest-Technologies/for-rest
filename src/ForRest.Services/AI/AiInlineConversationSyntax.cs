using System.Text;

namespace ForRest.Services.AI;

public enum AiInlineConversationLineKind
{
    Text,
    Blank,
    Comment,
    Prompt,
    Response,
    StaleResponse,
}

public sealed record AiInlineConversationLine(
    int LineNumber,
    string Text,
    AiInlineConversationLineKind Kind,
    string LeadingWhitespace,
    string Marker,
    string Content)
{
    public bool IsConversationLine => Kind is AiInlineConversationLineKind.Prompt
        or AiInlineConversationLineKind.Response
        or AiInlineConversationLineKind.StaleResponse;
}

public sealed record AiInlineConversationPrompt(
    AiInlineConversationLine PromptLine,
    IReadOnlyList<AiInlineConversationLine> BlockLines)
{
    public int LineNumber => PromptLine.LineNumber;

    public string PromptText => PromptLine.Content;

    public bool HasActiveResponse => BlockLines.Any(static line => line.Kind == AiInlineConversationLineKind.Response);

    public bool HasStaleResponse => BlockLines.Any(static line => line.Kind == AiInlineConversationLineKind.StaleResponse);

    public AiInlineConversationLine? LatestActiveResponse => BlockLines.LastOrDefault(static line => line.Kind == AiInlineConversationLineKind.Response);
}

public sealed record AiInlineConversationDocument(
    string SourceText,
    string LineEnding,
    bool HasTrailingNewline,
    IReadOnlyList<AiInlineConversationLine> Lines,
    IReadOnlyList<AiInlineConversationPrompt> Prompts)
{
    public AiInlineConversationPrompt? FindLatestPrompt(int cursorLineNumber)
    {
        if (cursorLineNumber < 1 || Prompts.Count == 0)
        {
            return null;
        }

        int clampedCursorLine = Math.Min(cursorLineNumber, Lines.Count);
        AiInlineConversationPrompt? prompt = null;
        foreach (AiInlineConversationPrompt candidate in Prompts)
        {
            if (candidate.LineNumber > clampedCursorLine)
            {
                break;
            }

            prompt = candidate;
        }

        return prompt;
    }
}

public static class AiInlineConversationPromptResolver
{
    public static AiInlineConversationPrompt? ResolveActionablePrompt(string sourceText, int cursorLineNumber)
    {
        return ResolveActionablePrompt(AiInlineConversationParser.Parse(sourceText), cursorLineNumber);
    }

    public static AiInlineConversationPrompt? ResolveActionablePrompt(AiInlineConversationDocument document, int cursorLineNumber)
    {
        ArgumentNullException.ThrowIfNull(document);

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
}

public static class AiInlineConversationParser
{
    public static AiInlineConversationDocument Parse(string sourceText)
    {
        string normalizedText = sourceText ?? string.Empty;
        (IReadOnlyList<ParsedLine> parsedLines, string lineEnding, bool hasTrailingNewline) = SplitLines(normalizedText);
        List<AiInlineConversationLine> lines = [];
        bool insideMultilineLiteral = false;
        foreach (ParsedLine parsedLine in parsedLines)
        {
            lines.Add(parsedLine.ToConversationLine(insideMultilineLiteral));
            if (CountTripleQuoteTokens(parsedLine.Text) % 2 == 1)
            {
                insideMultilineLiteral = !insideMultilineLiteral;
            }
        }

        List<AiInlineConversationPrompt> prompts = [];
        for (int index = 0; index < lines.Count; index++)
        {
            AiInlineConversationLine line = lines[index];
            if (line.Kind != AiInlineConversationLineKind.Prompt)
            {
                continue;
            }

            List<AiInlineConversationLine> blockLines = [];
            for (int blockIndex = index + 1; blockIndex < lines.Count; blockIndex++)
            {
                AiInlineConversationLine blockLine = lines[blockIndex];
                if (blockLine.Kind == AiInlineConversationLineKind.Prompt)
                {
                    break;
                }

                blockLines.Add(blockLine);
            }

            prompts.Add(new(line, blockLines));
        }

        return new(normalizedText, lineEnding, hasTrailingNewline, lines, prompts);
    }

    private static (IReadOnlyList<ParsedLine> Lines, string LineEnding, bool HasTrailingNewline) SplitLines(string text)
    {
        List<ParsedLine> lines = [];
        if (string.IsNullOrEmpty(text))
        {
            return (lines, Environment.NewLine, false);
        }

        int index = 0;
        int lineNumber = 1;
        string lineEnding = DetectLineEnding(text);
        while (index < text.Length)
        {
            int lineBreakIndex = index;
            while (lineBreakIndex < text.Length && text[lineBreakIndex] is not '\r' and not '\n')
            {
                lineBreakIndex++;
            }

            string lineText = text.Substring(index, lineBreakIndex - index);
            string? ending = null;
            if (lineBreakIndex < text.Length)
            {
                if (text[lineBreakIndex] == '\r' && lineBreakIndex + 1 < text.Length && text[lineBreakIndex + 1] == '\n')
                {
                    ending = "\r\n";
                    lineBreakIndex += 2;
                }
                else
                {
                    ending = text[lineBreakIndex].ToString();
                    lineBreakIndex++;
                }
            }

            lines.Add(new ParsedLine(lineNumber, lineText, ending));
            index = lineBreakIndex;
            lineNumber++;
        }

        bool hasTrailingNewline = lines.Count > 0 && lines[^1].Ending is not null;
        return (lines, lineEnding, hasTrailingNewline);
    }

    private static int CountTripleQuoteTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int count = 0;
        int index = 0;
        while ((index = text.IndexOf("\"\"\"", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += 3;
        }

        return count;
    }

    private static string DetectLineEnding(string text)
    {
        int index = text.IndexOf('\r');
        if (index >= 0)
        {
            return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
        }

        return text.IndexOf('\n') >= 0 ? "\n" : Environment.NewLine;
    }

    private sealed record ParsedLine(int LineNumber, string Text, string? Ending)
    {
        public AiInlineConversationLine ToConversationLine(bool insideMultilineLiteral)
        {
            string leadingWhitespace = GetLeadingWhitespace(Text);
            string trimmed = Text[leadingWhitespace.Length..];
            if (trimmed.Length == 0)
            {
                return new(LineNumber, Text, AiInlineConversationLineKind.Blank, leadingWhitespace, string.Empty, string.Empty);
            }

            if (insideMultilineLiteral)
            {
                return new(LineNumber, Text, AiInlineConversationLineKind.Text, leadingWhitespace, string.Empty, Text);
            }

            if (IsPrompt(trimmed))
            {
                return new(LineNumber, Text, AiInlineConversationLineKind.Prompt, leadingWhitespace, "##", trimmed[2..].TrimStart());
            }

            if (trimmed.StartsWith("#>", StringComparison.Ordinal))
            {
                return new(LineNumber, Text, AiInlineConversationLineKind.Response, leadingWhitespace, "#>", trimmed[2..].TrimStart());
            }

            if (trimmed.StartsWith("#~", StringComparison.Ordinal))
            {
                return new(LineNumber, Text, AiInlineConversationLineKind.StaleResponse, leadingWhitespace, "#~", trimmed[2..].TrimStart());
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                return new(LineNumber, Text, AiInlineConversationLineKind.Comment, leadingWhitespace, "#", trimmed[1..].TrimStart());
            }

            return new(LineNumber, Text, AiInlineConversationLineKind.Text, leadingWhitespace, string.Empty, Text);
        }

        private static bool IsPrompt(string text)
        {
            return text.Length >= 2 &&
                   text[0] == '#' &&
                   text[1] == '#' &&
                   (text.Length == 2 || text[2] != '#');
        }

        private static string GetLeadingWhitespace(string text)
        {
            int index = 0;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            return index == 0 ? string.Empty : text[..index];
        }
    }
}

public static class AiInlineConversationFormatter
{
    public static string ApplyResponse(string sourceText, int promptLineNumber, string responseText)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        if (document.Lines.Count == 0)
        {
            return sourceText ?? string.Empty;
        }

        int promptIndex = FindPromptIndex(document, promptLineNumber);
        if (promptIndex < 0)
        {
            return sourceText ?? string.Empty;
        }

        string renderedResponse = RenderResponseBlock(responseText, document.LineEnding);
        List<string> outputLines = [];
        for (int index = 0; index < document.Lines.Count; index++)
        {
            if (index != promptIndex)
            {
                if (index < promptIndex || document.Lines[index].Kind == AiInlineConversationLineKind.Prompt)
                {
                    outputLines.Add(document.Lines[index].Text);
                }

                continue;
            }

            outputLines.Add(document.Lines[index].Text);
            if (!string.IsNullOrWhiteSpace(renderedResponse))
            {
                outputLines.AddRange(renderedResponse.Split(document.LineEnding, StringSplitOptions.None));
            }

            int blockEnd = index + 1;
            while (blockEnd < document.Lines.Count && document.Lines[blockEnd].Kind != AiInlineConversationLineKind.Prompt)
            {
                outputLines.Add(RenderHistoryLine(document.Lines[blockEnd]));
                blockEnd++;
            }

            index = blockEnd - 1;
        }

        return JoinLines(outputLines, document.LineEnding, document.HasTrailingNewline);
    }

    public static string ApplyResponseAtCursor(string sourceText, int cursorLineNumber, string responseText)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        AiInlineConversationPrompt? prompt = document.FindLatestPrompt(cursorLineNumber);
        return prompt is null
            ? sourceText ?? string.Empty
            : ApplyResponse(sourceText, prompt.LineNumber, responseText);
    }

    public static string RemoveConversationLines(string sourceText)
    {
        return RewriteConversationLines(sourceText, preserveLineCount: false);
    }

    public static string BlankConversationLines(string sourceText)
    {
        return RewriteConversationLines(sourceText, preserveLineCount: true);
    }

    public static string EnsureFreshPromptAfterConversation(string sourceText, int promptLineNumber)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        if (document.Lines.Count == 0)
        {
            return sourceText ?? string.Empty;
        }

        List<string> lines = document.Lines.Select(static line => line.Text).ToList();
        int insertIndex = ResolveFreshPromptInsertIndex(document, promptLineNumber);
        if (HasBlankPromptAtIndex(document, insertIndex))
        {
            return sourceText ?? string.Empty;
        }

        for (int index = document.Lines.Count - 1; index >= 0; index--)
        {
            if (index == insertIndex)
            {
                continue;
            }

            if (document.Lines[index].Kind == AiInlineConversationLineKind.Prompt &&
                string.IsNullOrWhiteSpace(document.Lines[index].Content))
            {
                lines.RemoveAt(index);
                if (index < insertIndex)
                {
                    insertIndex--;
                }
            }
        }

        return InsertFreshPromptAtIndex(lines, insertIndex, document.LineEnding, document.HasTrailingNewline);
    }

    public static string ReplaceActiveResponse(string sourceText, int promptLineNumber, string responseText)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        if (document.Lines.Count == 0)
        {
            return sourceText ?? string.Empty;
        }

        int promptIndex = FindPromptIndex(document, promptLineNumber);
        if (promptIndex < 0)
        {
            return sourceText ?? string.Empty;
        }

        string renderedResponse = RenderResponseBlock(responseText, document.LineEnding);
        List<string> outputLines = [];
        for (int index = 0; index < document.Lines.Count; index++)
        {
            if (index != promptIndex)
            {
                outputLines.Add(document.Lines[index].Text);
                continue;
            }

            outputLines.Add(document.Lines[index].Text);
            if (!string.IsNullOrWhiteSpace(renderedResponse))
            {
                outputLines.AddRange(renderedResponse.Split(document.LineEnding, StringSplitOptions.None));
            }

            int blockEnd = index + 1;
            while (blockEnd < document.Lines.Count && document.Lines[blockEnd].Kind != AiInlineConversationLineKind.Prompt)
            {
                AiInlineConversationLine blockLine = document.Lines[blockEnd];
                if (blockLine.Kind != AiInlineConversationLineKind.Response)
                {
                    outputLines.Add(blockLine.Text);
                }

                blockEnd++;
            }

            index = blockEnd - 1;
        }

        return JoinLines(outputLines, document.LineEnding, document.HasTrailingNewline);
    }

    public static string FadePromptResponses(string sourceText, int promptLineNumber)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        if (document.Lines.Count == 0)
        {
            return sourceText ?? string.Empty;
        }

        int promptIndex = FindPromptIndex(document, promptLineNumber);
        if (promptIndex < 0)
        {
            return sourceText ?? string.Empty;
        }

        List<string> outputLines = [];
        for (int index = 0; index < document.Lines.Count; index++)
        {
            AiInlineConversationLine line = document.Lines[index];
            if (index == promptIndex)
            {
                outputLines.Add(line.Text);
                int blockEnd = index + 1;
                while (blockEnd < document.Lines.Count && document.Lines[blockEnd].Kind != AiInlineConversationLineKind.Prompt)
                {
                    outputLines.Add(RenderHistoryLine(document.Lines[blockEnd]));
                    blockEnd++;
                }

                index = blockEnd - 1;
                continue;
            }

            outputLines.Add(line.Text);
        }

        return JoinLines(outputLines, document.LineEnding, document.HasTrailingNewline);
    }

    public static string RenderResponseBlock(string responseText, string lineEnding = "\n")
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        string normalized = NormalizeLineEndings(responseText);
        string[] lines = normalized.Split('\n');
        return string.Join(
            lineEnding,
            lines.Select(static line => string.IsNullOrWhiteSpace(line) ? "#>" : $"#> {line}"));
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

        for (int index = promptLineNumber - 1; index >= 0; index--)
        {
            if (document.Lines[index].Kind == AiInlineConversationLineKind.Prompt)
            {
                return index;
            }
        }

        return -1;
    }

    private static string RenderHistoryLine(AiInlineConversationLine line)
    {
        return line.Kind switch
        {
            AiInlineConversationLineKind.Response => RenderPrefixedLine(line, "#~"),
            AiInlineConversationLineKind.StaleResponse => line.Text,
            _ => line.Text,
        };
    }

    private static string RenderPrefixedLine(AiInlineConversationLine line, string prefix)
    {
        return string.IsNullOrWhiteSpace(line.Content)
            ? $"{line.LeadingWhitespace}{prefix}"
            : $"{line.LeadingWhitespace}{prefix} {line.Content}";
    }

    private static int ResolveFreshPromptInsertIndex(AiInlineConversationDocument document, int promptLineNumber)
    {
        int promptIndex = FindPromptIndex(document, promptLineNumber);
        if (promptIndex >= 0)
        {
            int insertIndex = promptIndex + 1;
            while (insertIndex < document.Lines.Count &&
                   document.Lines[insertIndex].Kind is AiInlineConversationLineKind.Response or AiInlineConversationLineKind.StaleResponse)
            {
                insertIndex++;
            }

            return insertIndex;
        }

        int lastResponseIndex = FindLastResponseIndex(document);
        if (lastResponseIndex >= 0)
        {
            return lastResponseIndex + 1;
        }

        return Math.Clamp(promptLineNumber - 1, 0, document.Lines.Count);
    }

    private static bool HasBlankPromptAtIndex(AiInlineConversationDocument document, int insertIndex)
    {
        return insertIndex >= 0 &&
               insertIndex < document.Lines.Count &&
               document.Lines[insertIndex].Kind == AiInlineConversationLineKind.Prompt &&
               string.IsNullOrWhiteSpace(document.Lines[insertIndex].Content);
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

    private static string InsertFreshPromptAtIndex(
        List<string> lines,
        int insertIndex,
        string lineEnding,
        bool hasTrailingNewline)
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

        return JoinLines(lines, lineEnding, hasTrailingNewline);
    }

    private static string RewriteConversationLines(string sourceText, bool preserveLineCount)
    {
        AiInlineConversationDocument document = AiInlineConversationParser.Parse(sourceText);
        if (document.Lines.Count == 0)
        {
            return sourceText ?? string.Empty;
        }

        List<string> outputLines = [];
        foreach (AiInlineConversationLine line in document.Lines)
        {
            if (line.Kind is AiInlineConversationLineKind.Prompt
                or AiInlineConversationLineKind.Response
                or AiInlineConversationLineKind.StaleResponse)
            {
                if (preserveLineCount)
                {
                    outputLines.Add(string.Empty);
                }

                continue;
            }

            outputLines.Add(line.Text);
        }

        return JoinLines(outputLines, document.LineEnding, document.HasTrailingNewline);
    }

    private static string JoinLines(IReadOnlyList<string> lines, string lineEnding, bool hasTrailingNewline)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        for (int index = 0; index < lines.Count; index++)
        {
            builder.Append(lines[index]);
            if (index + 1 < lines.Count || hasTrailingNewline)
            {
                builder.Append(lineEnding);
            }
        }

        return builder.ToString();
    }

    private static string NormalizeLineEndings(string value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
