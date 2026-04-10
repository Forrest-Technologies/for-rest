namespace ForRest.Services.AI;

public enum AiProviderKind
{
    OpenAI,
    AzureOpenAI,
    Grok,
    Groq,
    DeepSeek,
    Mistral,
    OpenRouter,
    GoogleGemini,
    Anthropic,
    Custom,
}

public static class AiProviderDefaults
{
    public const string OpenAiEndpoint = "https://api.openai.com/v1";
    public const string GrokEndpoint = "https://api.x.ai/v1";
    public const string GroqEndpoint = "https://api.groq.com/openai/v1";
    public const string DeepSeekEndpoint = "https://api.deepseek.com/v1";
    public const string MistralEndpoint = "https://api.mistral.ai/v1";
    public const string OpenRouterEndpoint = "https://openrouter.ai/api/v1";
    public const string GoogleGeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai";
    public const string AnthropicEndpoint = "https://api.anthropic.com/v1";

    public static string? GetDefaultEndpoint(AiProviderKind providerKind)
    {
        return providerKind switch
        {
            AiProviderKind.Grok => GrokEndpoint,
            AiProviderKind.Groq => GroqEndpoint,
            AiProviderKind.DeepSeek => DeepSeekEndpoint,
            AiProviderKind.Mistral => MistralEndpoint,
            AiProviderKind.OpenRouter => OpenRouterEndpoint,
            AiProviderKind.GoogleGemini => GoogleGeminiEndpoint,
            AiProviderKind.Anthropic => AnthropicEndpoint,
            _ => null,
        };
    }

    public static AiConversationTransport GetPreferredTransport(AiProviderKind providerKind)
    {
        // The Responses API is only broadly supported by OpenAI, Azure OpenAI, Grok/xAI,
        // and Google Gemini's OpenAI-compat layer. Everyone else should default to
        // chat completions so users who pick a provider and forget to set the transport
        // still get a working round-trip.
        return providerKind switch
        {
            AiProviderKind.OpenAI => AiConversationTransport.Responses,
            AiProviderKind.AzureOpenAI => AiConversationTransport.Responses,
            AiProviderKind.Grok => AiConversationTransport.Responses,
            AiProviderKind.GoogleGemini => AiConversationTransport.Responses,
            _ => AiConversationTransport.ChatCompletions,
        };
    }
}

public enum AiConversationTransport
{
    Responses,
    ChatCompletions,
}

public sealed record AiSecretSetting
{
    public const string MaskedValue = "********";

    public string SettingKey { get; init; } = "ai.api_key";

    public string Value { get; init; } = string.Empty;

    public bool IsConfigured { get; init; }

    public bool HasUsableValue => !string.IsNullOrWhiteSpace(Value);

    public string DisplayValue => (IsConfigured || HasUsableValue) ? MaskedValue : string.Empty;
}

public sealed record AiProviderSettings
{
    public AiProviderKind ProviderKind { get; init; } = AiProviderKind.OpenAI;

    public AiConversationTransport Transport { get; init; } = AiConversationTransport.Responses;

    public string Endpoint { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string DeploymentName { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> CustomHeaders { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record AiToolSettings
{
    public bool EnableDocsSearch { get; init; } = true;

    public bool EnableDocumentPatch { get; init; } = true;

    public int MaxSearchResults { get; init; } = 5;

    public int MaxPatchOperations { get; init; } = 8;

    public int MaxPatchCharacters { get; init; } = 8_192;
}

public sealed record AiConversationSettings
{
    public int IdleTimeoutMinutes { get; init; } = 15;

    public int MaxHistoryTurns { get; init; } = 12;

    public int MaxClarificationTurns { get; init; } = 2;

    public int ExecutionTimeoutSeconds { get; init; } = 45;

    public bool StreamResponses { get; init; } = true;
}

public sealed record AiSettings
{
    public bool Enabled { get; init; }

    public AiProviderSettings Provider { get; init; } = new();

    public AiSecretSetting ApiKey { get; init; } = new();

    public AiToolSettings Tools { get; init; } = new();

    public AiConversationSettings Conversation { get; init; } = new();

    public string? SystemPromptPrefix { get; init; }

    public bool ShouldShowSettingsSection => Enabled;

    public bool ShouldShowSecretEditor => Enabled;

    public AiSettingsEditorProjection ToEditorProjection()
    {
        return new(
            Settings: this,
            ApiKeyEditorValue: ApiKey.DisplayValue,
            Visible: Enabled);
    }
}

public sealed record AiSettingsEditorProjection(
    AiSettings Settings,
    string ApiKeyEditorValue,
    bool Visible);
