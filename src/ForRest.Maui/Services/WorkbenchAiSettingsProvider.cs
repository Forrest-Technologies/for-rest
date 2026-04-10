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
		AiProviderKind providerKind = ParseProvider(settings.Provider);
		return new AiSettings
		{
			Enabled = settings.Enabled,
			Provider = new AiProviderSettings
			{
				ProviderKind = providerKind,
				Transport = ParseTransport(settings.Api),
				Endpoint = NormalizeEndpoint(settings.Endpoint, providerKind),
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
		return value?.Trim().ToLowerInvariant() switch
		{
			"azure_openai" or "azure-openai" or "azure" => AiProviderKind.AzureOpenAI,
			"grok" or "xai" or "x_ai" or "x-ai" or "x.ai" => AiProviderKind.Grok,
			_ => AiProviderKind.OpenAI,
		};
	}

	private static AiConversationTransport ParseTransport(string value)
	{
		return value?.Trim().ToLowerInvariant() switch
		{
			"chat" or "chat_completions" => AiConversationTransport.ChatCompletions,
			_ => AiConversationTransport.Responses,
		};
	}

	private static string NormalizeEndpoint(string value, AiProviderKind providerKind)
	{
		string trimmed = (value ?? string.Empty).Trim();
		if (trimmed.Length == 0)
		{
			return providerKind == AiProviderKind.Grok
				? AiProviderDefaults.GrokEndpoint
				: string.Empty;
		}

		trimmed = trimmed.TrimEnd('/');

		// Users often paste the full per-transport URL from provider docs
		// (e.g. https://api.x.ai/v1/responses). The OpenAI-compatible SDK
		// treats Endpoint as the base URI and appends /responses or
		// /chat/completions itself, so we strip the trailing transport
		// segment to keep the base URI correct.
		string[] transportSuffixes =
		[
			"/chat/completions",
			"/responses",
		];

		foreach (string suffix in transportSuffixes)
		{
			if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				trimmed = trimmed[..^suffix.Length].TrimEnd('/');
				break;
			}
		}

		return trimmed;
	}
}
