namespace ForRest.Services.AI;

public sealed record AiToolDescriptor(
    string Name,
    string Description,
    string InvocationHint,
    bool MutatesDocument);

public sealed record AiPromptTopic(
    string Title,
    string Summary,
    string Source,
    string Content);

public sealed record AiPromptManifest(
    string SystemPrompt,
    IReadOnlyList<AiToolDescriptor> Tools,
    IReadOnlyList<AiPromptTopic> Topics);

public interface IAiPromptManifestBuilder
{
    AiPromptManifest Build(
        AiSettings settings,
        string objective,
        IEnumerable<AiToolDescriptor> tools,
        IEnumerable<AiPromptTopic> topics);
}

public sealed class AiPromptManifestBuilder : IAiPromptManifestBuilder
{
    public AiPromptManifest Build(
        AiSettings settings,
        string objective,
        IEnumerable<AiToolDescriptor> tools,
        IEnumerable<AiPromptTopic> topics)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(topics);

        List<AiToolDescriptor> toolList = [.. tools];
        List<AiPromptTopic> topicList = [.. topics];
        StringBuilder prompt = new();
        prompt.AppendLine("You are the For-Rest AI assistant.");

        if (!string.IsNullOrWhiteSpace(settings.SystemPromptPrefix))
        {
            prompt.AppendLine(settings.SystemPromptPrefix.Trim());
            prompt.AppendLine();
        }

        prompt.AppendLine($"AI enabled: {(settings.Enabled ? "yes" : "no")}");
        prompt.AppendLine($"Provider: {settings.Provider.ProviderKind}");
        prompt.AppendLine($"Transport: {settings.Provider.Transport}");
        prompt.AppendLine($"Model: {NullToPlaceholder(settings.Provider.Model)}");
        prompt.AppendLine($"Endpoint: {DescribeEndpoint(settings.Provider.Endpoint)}");
        prompt.AppendLine($"Deployment: {NullToPlaceholder(settings.Provider.DeploymentName)}");
        prompt.AppendLine($"API key configured: {(settings.ApiKey.IsConfigured ? "yes" : "no")}");
        prompt.AppendLine();
        prompt.AppendLine("Operating rules:");
        prompt.AppendLine("- Keep responses brief and practical.");
        prompt.AppendLine("- Prefer the local docs search tool before guessing about language or app behavior.");
        prompt.AppendLine("- Read the active document before editing so you can inspect the current source text and compiler diagnostics.");
        prompt.AppendLine("- If the active document has syntax or compilation errors, use those diagnostics plus local docs to fix the request.");
        prompt.AppendLine("- Only patch documents when the user asked for an edit or when a correction is clearly required.");
        prompt.AppendLine("- Keep patch operations bounded and explicit.");
        prompt.AppendLine("- When the user asked to rewrite the request from scratch, prefer replace_active_document over patch_active_document.");
        prompt.AppendLine("- When editing the active request, leave it runnable when you finish.");
        prompt.AppendLine("- Modify the existing request in place. Do not append duplicate request blocks unless the user explicitly asked for a second example.");
        prompt.AppendLine("- Once the request is actionable, prefer making the edit over continuing the chat.");
        prompt.AppendLine("- Inline IDE mode is not a questionnaire. If the user asked you to fix, rewrite, or improve the request, choose a reasonable default and do the work.");
        prompt.AppendLine("- Use only documented ForRest syntax. If a construct is not in the local docs, do not invent it.");
        prompt.AppendLine("- Use only `#` comments on their own lines. Do not use `//` comments and do not append trailing inline comments after code.");
        prompt.AppendLine("- `expect` statements are top-level assertions. Do not put `expect` inside `if`, `else`, `foreach`, or `while` blocks.");
        prompt.AppendLine("- Do not ask the user to paste working syntax, grammar examples, or line numbers if the active document diagnostics or local docs can answer it.");
        prompt.AppendLine("- Do not ask the user whether ForRest supports a syntax or helper that the local docs or active document can confirm.");
        prompt.AppendLine("- Do not ask multiple-choice follow-up questions unless the request is truly blocked on a real product decision the user must make.");
        prompt.AppendLine("- If the request already contains working `expect` lines, preserve their exact grammar unless you are only moving them back to top-level.");
        prompt.AppendLine("- If an edit is rejected, read the active document again, inspect the latest diagnostics, and try one corrected edit before asking the user for more information.");
        prompt.AppendLine("- If an active document topic is present below, treat its source and diagnostics as the current truth. Do not ask the user to paste them again.");
        prompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(objective))
        {
            prompt.AppendLine($"Current objective: {objective.Trim()}");
            prompt.AppendLine();
        }

        if (toolList.Count > 0)
        {
            prompt.AppendLine("Available tools:");
            foreach (AiToolDescriptor tool in toolList)
            {
                prompt.AppendLine($"- {tool.Name}: {tool.Description}");
                prompt.AppendLine($"  Hint: {tool.InvocationHint}");
            }

            prompt.AppendLine();
        }

        if (topicList.Count > 0)
        {
            prompt.AppendLine("Knowledge base:");
            foreach (AiPromptTopic topic in topicList)
            {
                prompt.AppendLine($"- {topic.Title} ({topic.Source})");
                prompt.AppendLine($"  {topic.Summary}");
                if (!string.IsNullOrWhiteSpace(topic.Content))
                {
                    prompt.AppendLine($"  {TrimContent(topic)}");
                }
            }
        }

        return new(prompt.ToString().TrimEnd(), toolList, topicList);
    }

    private static string NullToPlaceholder(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(not set)" : value.Trim();
    }

    private static string DescribeEndpoint(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(not set)" : "(configured)";
    }

    private static string TrimContent(AiPromptTopic topic)
    {
        string text = (topic.Content ?? string.Empty).Trim();
        int limit = string.Equals(topic.Source, "active-document", StringComparison.OrdinalIgnoreCase)
            ? 2000
            : 240;
        return text.Length <= limit ? text : text[..limit].TrimEnd();
    }
}
