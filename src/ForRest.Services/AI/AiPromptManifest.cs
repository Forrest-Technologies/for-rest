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
        prompt.AppendLine("- Only patch documents when the user asked for an edit or when a correction is clearly required.");
        prompt.AppendLine("- Keep patch operations bounded and explicit.");
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
                    prompt.AppendLine($"  {TrimContent(topic.Content)}");
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

    private static string TrimContent(string value)
    {
        string text = value.Trim();
        return text.Length <= 240 ? text : text[..240].TrimEnd();
    }
}

