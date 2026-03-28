namespace ForRest.Services.AI;

public enum AiProviderKind
{
    OpenAI,
    AzureOpenAI,
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
