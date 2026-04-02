namespace ForRest.Services.AI;

public enum AiSettingsIssueSeverity
{
    Warning,
    Error,
}

public sealed record AiSettingsIssue(
    AiSettingsIssueSeverity Severity,
    string Code,
    string Message);

public interface IAiSettingsValidator
{
    IReadOnlyList<AiSettingsIssue> Validate(AiSettings settings);
}

public sealed class AiSettingsValidator : IAiSettingsValidator
{
    public IReadOnlyList<AiSettingsIssue> Validate(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        List<AiSettingsIssue> issues = [];
        if (!settings.Enabled)
        {
            return issues;
        }

        if (settings.Provider.ProviderKind == AiProviderKind.AzureOpenAI)
        {
            ValidateEndpoint(settings.Provider.Endpoint, issues);

            if (string.IsNullOrWhiteSpace(settings.Provider.DeploymentName))
            {
                issues.Add(new(AiSettingsIssueSeverity.Error, "ai.azure.deployment.required", "Azure OpenAI deployment name is required when AI is enabled."));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Provider.Model))
            {
                issues.Add(new(AiSettingsIssueSeverity.Error, "ai.openai.model.required", "OpenAI model is required when AI is enabled."));
            }
        }

        if (!settings.ApiKey.IsConfigured)
        {
            issues.Add(new(AiSettingsIssueSeverity.Error, "ai.api-key.required", "An API key must be configured when AI is enabled."));
        }

        if (settings.Tools.MaxSearchResults is < 1 or > 20)
        {
            issues.Add(new(AiSettingsIssueSeverity.Error, "ai.tools.search-results.out-of-range", "Max search results must be between 1 and 20."));
        }

        if (settings.Tools.MaxPatchOperations is < 1 or > 32)
        {
            issues.Add(new(AiSettingsIssueSeverity.Error, "ai.tools.patch-operations.out-of-range", "Max patch operations must be between 1 and 32."));
        }

        if (settings.Tools.MaxPatchCharacters is < 256 or > 32_768)
        {
            issues.Add(new(AiSettingsIssueSeverity.Error, "ai.tools.patch-size.out-of-range", "Max patch characters must be between 256 and 32768."));
        }

        if (settings.Conversation.IdleTimeoutMinutes < 1)
        {
            issues.Add(new(AiSettingsIssueSeverity.Error, "ai.conversation.timeout.out-of-range", "Conversation idle timeout must be at least 1 minute."));
        }

        if (settings.Conversation.MaxHistoryTurns < 1)
        {
            issues.Add(new(AiSettingsIssueSeverity.Error, "ai.conversation.history.out-of-range", "Conversation history must keep at least 1 turn."));
        }

        if (settings.Conversation.MaxClarificationTurns < 0)
        {
            issues.Add(new(AiSettingsIssueSeverity.Error, "ai.conversation.clarification-turns.out-of-range", "Max clarification turns must be zero or greater."));
        }

        if (settings.Conversation.ExecutionTimeoutSeconds is < 1 or > 300)
        {
            issues.Add(new(AiSettingsIssueSeverity.Error, "ai.conversation.execution-timeout.out-of-range", "AI request timeout must be between 1 and 300 seconds."));
        }

        return issues;
    }

    private static void ValidateEndpoint(string endpoint, List<AiSettingsIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            issues.Add(new(AiSettingsIssueSeverity.Error, "ai.azure.endpoint.required", "Azure OpenAI endpoint is required when AI is enabled."));
            return;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsed) ||
            (parsed.Scheme is not "https" and not "http"))
        {
            issues.Add(new(AiSettingsIssueSeverity.Error, "ai.azure.endpoint.invalid", "Azure OpenAI endpoint must be an absolute HTTP or HTTPS URL."));
        }
    }
}
