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

        AiProviderKind providerKind = settings.Provider.ProviderKind;
        if (providerKind == AiProviderKind.AzureOpenAI)
        {
            ValidateEndpoint(settings.Provider.Endpoint, issues, required: true);

            if (string.IsNullOrWhiteSpace(settings.Provider.DeploymentName))
            {
                issues.Add(new(AiSettingsIssueSeverity.Error, "ai.azure.deployment.required", "Azure OpenAI deployment name is required when AI is enabled."));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Provider.Model))
            {
                (string code, string message) = providerKind switch
                {
                    AiProviderKind.Grok => ("ai.grok.model.required", "Grok model is required when AI is enabled."),
                    AiProviderKind.Groq => ("ai.groq.model.required", "Groq model is required when AI is enabled."),
                    AiProviderKind.DeepSeek => ("ai.deepseek.model.required", "DeepSeek model is required when AI is enabled."),
                    AiProviderKind.Mistral => ("ai.mistral.model.required", "Mistral model is required when AI is enabled."),
                    AiProviderKind.OpenRouter => ("ai.openrouter.model.required", "OpenRouter model is required when AI is enabled."),
                    AiProviderKind.GoogleGemini => ("ai.gemini.model.required", "Google Gemini model is required when AI is enabled."),
                    AiProviderKind.Anthropic => ("ai.anthropic.model.required", "Anthropic model is required when AI is enabled."),
                    AiProviderKind.Custom => ("ai.custom.model.required", "Custom provider model is required when AI is enabled."),
                    _ => ("ai.openai.model.required", "OpenAI model is required when AI is enabled."),
                };
                issues.Add(new(AiSettingsIssueSeverity.Error, code, message));
            }

            bool endpointRequired = providerKind == AiProviderKind.Custom;
            if (endpointRequired && string.IsNullOrWhiteSpace(settings.Provider.Endpoint))
            {
                issues.Add(new(AiSettingsIssueSeverity.Error, "ai.custom.endpoint.required", "Custom provider endpoint is required when AI is enabled."));
            }
            else
            {
                ValidateEndpoint(settings.Provider.Endpoint, issues, required: false);
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

    private static void ValidateEndpoint(string endpoint, List<AiSettingsIssue> issues, bool required)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            if (required)
            {
                issues.Add(new(AiSettingsIssueSeverity.Error, "ai.azure.endpoint.required", "Azure OpenAI endpoint is required when AI is enabled."));
            }

            return;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsed) ||
            (parsed.Scheme is not "https" and not "http"))
        {
            string code = required ? "ai.azure.endpoint.invalid" : "ai.endpoint.invalid";
            string message = required
                ? "Azure OpenAI endpoint must be an absolute HTTP or HTTPS URL."
                : "AI endpoint must be an absolute HTTP or HTTPS URL.";
            issues.Add(new(AiSettingsIssueSeverity.Error, code, message));
        }
    }
}
