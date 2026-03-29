using ForRest.Maui.Theming;
using ForRest.Services.AI;

namespace ForRest.Maui.Services;

public interface IWorkbenchAiSettingsProvider
{
	AiSettings GetCurrentSettings();
}

public sealed class WorkbenchAiSettingsProvider : IWorkbenchAiSettingsProvider
{
	private readonly ThemeConfigStore _themeConfigStore;
	private readonly ThemeConfigParser _themeConfigParser;

	public WorkbenchAiSettingsProvider(
		ThemeConfigStore themeConfigStore,
		ThemeConfigParser themeConfigParser)
	{
		_themeConfigStore = themeConfigStore;
		_themeConfigParser = themeConfigParser;
	}

	public AiSettings GetCurrentSettings()
	{
		string rawText = _themeConfigStore.ReadAllText();
		if (string.IsNullOrWhiteSpace(rawText))
		{
			return new AiSettings();
		}

		ThemeConfigDocument document = _themeConfigParser.Parse(rawText);
		return document.Ai.ToRuntimeSettings();
	}
}

internal static class ForRestAiSettingsMapper
{
	public static AiSettings ToRuntimeSettings(this ForRestAiSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		string apiKey = settings.ApiKey.Trim();
		return new AiSettings
		{
			Enabled = settings.Enabled,
			Provider = new AiProviderSettings
			{
				ProviderKind = ParseProvider(settings.Provider),
				Transport = ParseTransport(settings.Api),
				Endpoint = settings.Endpoint.Trim(),
				Model = settings.Model.Trim(),
				DeploymentName = settings.DeploymentName.Trim(),
			},
			ApiKey = new AiSecretSetting
			{
				Value = apiKey,
				IsConfigured = !string.IsNullOrWhiteSpace(apiKey),
			},
			Conversation = new AiConversationSettings
			{
				StreamResponses = settings.StreamResponses,
			},
			SystemPromptPrefix = string.IsNullOrWhiteSpace(settings.SystemPrompt)
				? null
				: settings.SystemPrompt.Trim(),
		};
	}

	private static AiProviderKind ParseProvider(string value)
	{
		return string.Equals(value?.Trim(), "azure_openai", StringComparison.OrdinalIgnoreCase)
			? AiProviderKind.AzureOpenAI
			: AiProviderKind.OpenAI;
	}

	private static AiConversationTransport ParseTransport(string value)
	{
		return value?.Trim().ToLowerInvariant() switch
		{
			"chat" or "chat_completions" => AiConversationTransport.ChatCompletions,
			_ => AiConversationTransport.Responses,
		};
	}
}
