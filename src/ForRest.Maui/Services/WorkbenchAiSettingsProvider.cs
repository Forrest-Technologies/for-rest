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
				Transport = ParseTransport(settings.Api, providerKind),
				Endpoint = NormalizeEndpoint(settings.Endpoint, providerKind),
				Model = settings.Model.Trim(),
				DeploymentName = settings.DeploymentName.Trim(),
				CustomHeaders = ParseCustomHeaders(settings.CustomHeaders),
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
			"groq" => AiProviderKind.Groq,
			"deepseek" or "deep_seek" or "deep-seek" => AiProviderKind.DeepSeek,
			"mistral" or "mistralai" or "mistral-ai" or "mistral_ai" => AiProviderKind.Mistral,
			"openrouter" or "open_router" or "open-router" => AiProviderKind.OpenRouter,
			"gemini" or "google" or "google_gemini" or "google-gemini" => AiProviderKind.GoogleGemini,
			"anthropic" or "claude" => AiProviderKind.Anthropic,
			"custom" or "openai_compatible" or "openai-compatible" => AiProviderKind.Custom,
			_ => AiProviderKind.OpenAI,
		};
	}

	private static AiConversationTransport ParseTransport(string value, AiProviderKind providerKind)
	{
		string? normalized = value?.Trim().ToLowerInvariant();
		return normalized switch
		{
			"chat" or "chat_completions" or "chat-completions" or "completions" => AiConversationTransport.ChatCompletions,
			"responses" or "response" => AiConversationTransport.Responses,
			_ => AiProviderDefaults.GetPreferredTransport(providerKind),
		};
	}

	private static string NormalizeEndpoint(string value, AiProviderKind providerKind)
	{
		string trimmed = (value ?? string.Empty).Trim();
		if (trimmed.Length == 0)
		{
			return AiProviderDefaults.GetDefaultEndpoint(providerKind) ?? string.Empty;
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

	private static IReadOnlyDictionary<string, string> ParseCustomHeaders(string raw)
	{
		Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(raw))
		{
			return headers;
		}

		// Accept `Name: value` pairs separated by `;` or newlines so the
		// TOML scalar stays single-line-safe (see ThemeConfigParser).
		string[] pairs = raw.Split(
			[';', '\n', '\r'],
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		foreach (string pair in pairs)
		{
			int separator = pair.IndexOf(':');
			if (separator <= 0)
			{
				continue;
			}

			string name = pair[..separator].Trim();
			string value = pair[(separator + 1)..].Trim();
			if (name.Length == 0)
			{
				continue;
			}

			headers[name] = value;
		}

		return headers;
	}
}
