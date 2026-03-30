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
        "make",
        "create",
        "build",
        "generate",
    };

    public static bool IsLikelyEditPrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        string[] tokens = Tokenize(prompt);
        bool hasEditKeyword = tokens.Any(EditIntentKeywords.Contains);
        if (!hasEditKeyword)
        {
            return false;
        }

        if (ContainsSequence(tokens, "want", "you", "to"))
        {
            return true;
        }

        return !LooksLikeInformationalLead(tokens);
    }

    private static string[] Tokenize(string prompt)
    {
        return prompt.ToLowerInvariant().Split(
            [' ', '\t', '\r', '\n', '.', ',', '!', '?', ':', ';', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-'],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool LooksLikeInformationalLead(string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return false;
        }

        return tokens[0] switch
        {
            "explain" or "describe" or "summarize" or "clarify" => true,
            "help" => tokens.Length > 2 && tokens[1] == "me" && tokens[2] == "understand",
            "what" => tokens.Length > 1 && (tokens[1] == "does" || tokens[1] == "is"),
            "why" => tokens.Length > 1 && (tokens[1] == "does" || tokens[1] == "is"),
            "how" => tokens.Length > 1 && (tokens[1] == "do" || tokens[1] == "does" || tokens[1] == "can" || tokens[1] == "to"),
            _ => false,
        };
    }

    private static bool ContainsSequence(string[] tokens, params string[] sequence)
    {
        if (sequence.Length == 0 || tokens.Length < sequence.Length)
        {
            return false;
        }

        for (int start = 0; start <= tokens.Length - sequence.Length; start++)
        {
            bool match = true;
            for (int index = 0; index < sequence.Length; index++)
            {
                if (!string.Equals(tokens[start + index], sequence[index], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
