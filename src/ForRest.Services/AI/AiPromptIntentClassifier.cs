namespace ForRest.Services.AI;

internal static class AiPromptIntentClassifier
{
    private static readonly HashSet<string> EditIntentKeywords = new(StringComparer.Ordinal)
    {
        "fix",
        "rewrite",
        "update",
        "change",
        "modify",
        "improve",
        "add",
        "remove",
        "replace",
        "patch",
        "edit",
        "refactor",
        "repair",
        "convert",
        "showcase",
        "enumerate",
        "iterate",
        "loop",
        "batch",
        "stash",
        "capture",
        "collect",
        "store",
        "expand",
        "transform",
    };

    private static readonly HashSet<string> InformationalIntentKeywords = new(StringComparer.Ordinal)
    {
        "explain",
        "describe",
        "summarize",
        "what",
        "why",
        "how",
        "understand",
        "clarify",
    };

    public static bool IsLikelyEditPrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        string[] tokens = prompt.ToLowerInvariant().Split(
            [' ', '\t', '\r', '\n', '.', ',', '!', '?', ':', ';', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-'],
            StringSplitOptions.RemoveEmptyEntries);
        bool hasInformationalKeyword = tokens.Any(InformationalIntentKeywords.Contains);
        bool hasEditKeyword = tokens.Any(EditIntentKeywords.Contains);
        return hasEditKeyword && !hasInformationalKeyword;
    }
}
