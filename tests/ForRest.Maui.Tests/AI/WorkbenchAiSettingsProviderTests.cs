using System;
using System.IO;
using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using ForRest.Services.AI;

namespace ForRest.Maui.Tests.AI;

[TestClass]
public sealed class WorkbenchAiSettingsProviderTests
{
	[TestMethod]
	public void ToRuntimeSettings_maps_provider_transport_and_secret_state()
	{
		ForRestAiSettings settings = new(
			Enabled: true,
			Provider: "azure_openai",
			Api: "chat",
			Endpoint: "https://example.openai.azure.com",
			Model: "gpt-4.1-mini",
			DeploymentName: "forrest-agent",
			ApiKey: "test-key",
			SystemPrompt: "Keep answers brief.",
			StreamResponses: false);

		AiSettings runtime = settings.ToRuntimeSettings();

		Assert.IsTrue(runtime.Enabled);
		Assert.AreEqual(AiProviderKind.AzureOpenAI, runtime.Provider.ProviderKind);
		Assert.AreEqual(AiConversationTransport.ChatCompletions, runtime.Provider.Transport);
		Assert.AreEqual("https://example.openai.azure.com", runtime.Provider.Endpoint);
		Assert.AreEqual("forrest-agent", runtime.Provider.DeploymentName);
		Assert.AreEqual("test-key", runtime.ApiKey.Value);
		Assert.IsTrue(runtime.ApiKey.IsConfigured);
		Assert.IsFalse(runtime.Conversation.StreamResponses);
		Assert.AreEqual("Keep answers brief.", runtime.SystemPromptPrefix);
	}

	[TestMethod]
	public void ToRuntimeSettings_maps_grok_provider_and_defaults_endpoint()
	{
		ForRestAiSettings settings = new(
			Enabled: true,
			Provider: "grok",
			Api: "responses",
			Endpoint: "",
			Model: "grok-4-fast-non-reasoning",
			ApiKey: "xai-key");

		AiSettings runtime = settings.ToRuntimeSettings();

		Assert.AreEqual(AiProviderKind.Grok, runtime.Provider.ProviderKind);
		Assert.AreEqual(AiConversationTransport.Responses, runtime.Provider.Transport);
		Assert.AreEqual("https://api.x.ai/v1", runtime.Provider.Endpoint);
		Assert.AreEqual("grok-4-fast-non-reasoning", runtime.Provider.Model);
	}

	[TestMethod]
	public void ToRuntimeSettings_strips_transport_suffix_from_endpoint()
	{
		ForRestAiSettings settings = new(
			Enabled: true,
			Provider: "grok",
			Api: "responses",
			Endpoint: "https://api.x.ai/v1/responses",
			Model: "grok-4-fast-non-reasoning",
			ApiKey: "xai-key");

		AiSettings runtime = settings.ToRuntimeSettings();

		Assert.AreEqual("https://api.x.ai/v1", runtime.Provider.Endpoint);
	}

	[TestMethod]
	public void ToRuntimeSettings_strips_chat_completions_suffix_from_endpoint()
	{
		ForRestAiSettings settings = new(
			Enabled: true,
			Provider: "openai",
			Api: "chat",
			Endpoint: "https://api.openai.com/v1/chat/completions/",
			Model: "gpt-4.1-mini",
			ApiKey: "test-key");

		AiSettings runtime = settings.ToRuntimeSettings();

		Assert.AreEqual("https://api.openai.com/v1", runtime.Provider.Endpoint);
	}

	[TestMethod]
	public void ToRuntimeSettings_recognizes_xai_provider_aliases()
	{
		foreach (string alias in new[] { "xai", "x-ai", "x_ai", "x.ai", "GROK" })
		{
			ForRestAiSettings settings = new(
				Enabled: true,
				Provider: alias,
				Api: "responses",
				Model: "grok-4-fast-non-reasoning",
				ApiKey: "xai-key");

			AiSettings runtime = settings.ToRuntimeSettings();

			Assert.AreEqual(AiProviderKind.Grok, runtime.Provider.ProviderKind, $"Alias '{alias}' should map to Grok.");
		}
	}

	[TestMethod]
	public void ToRuntimeSettings_maps_each_builtin_provider_to_default_endpoint()
	{
		(string providerName, AiProviderKind expectedKind, string expectedEndpoint, AiConversationTransport expectedTransport)[] cases =
		[
			("openai", AiProviderKind.OpenAI, "", AiConversationTransport.Responses),
			("groq", AiProviderKind.Groq, "https://api.groq.com/openai/v1", AiConversationTransport.ChatCompletions),
			("deepseek", AiProviderKind.DeepSeek, "https://api.deepseek.com/v1", AiConversationTransport.ChatCompletions),
			("mistral", AiProviderKind.Mistral, "https://api.mistral.ai/v1", AiConversationTransport.ChatCompletions),
			("openrouter", AiProviderKind.OpenRouter, "https://openrouter.ai/api/v1", AiConversationTransport.ChatCompletions),
			("gemini", AiProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com/v1beta/openai", AiConversationTransport.Responses),
			("anthropic", AiProviderKind.Anthropic, "https://api.anthropic.com/v1", AiConversationTransport.ChatCompletions),
		];

		foreach ((string providerName, AiProviderKind expectedKind, string expectedEndpoint, AiConversationTransport expectedTransport) in cases)
		{
			ForRestAiSettings settings = new(
				Enabled: true,
				Provider: providerName,
				Api: "",
				Endpoint: "",
				Model: "test-model",
				ApiKey: "k");

			AiSettings runtime = settings.ToRuntimeSettings();

			Assert.AreEqual(expectedKind, runtime.Provider.ProviderKind, providerName);
			Assert.AreEqual(expectedEndpoint, runtime.Provider.Endpoint, providerName);
			Assert.AreEqual(expectedTransport, runtime.Provider.Transport, providerName);
		}
	}

	[TestMethod]
	public void ToRuntimeSettings_custom_provider_preserves_user_endpoint()
	{
		ForRestAiSettings settings = new(
			Enabled: true,
			Provider: "custom",
			Api: "chat",
			Endpoint: "https://ai-gateway.example.internal/v1",
			Model: "gpt-4o-mini",
			ApiKey: "gw");

		AiSettings runtime = settings.ToRuntimeSettings();

		Assert.AreEqual(AiProviderKind.Custom, runtime.Provider.ProviderKind);
		Assert.AreEqual("https://ai-gateway.example.internal/v1", runtime.Provider.Endpoint);
		Assert.AreEqual(AiConversationTransport.ChatCompletions, runtime.Provider.Transport);
	}

	[TestMethod]
	public void ToRuntimeSettings_parses_custom_headers_from_semicolon_separated_string()
	{
		ForRestAiSettings settings = new(
			Enabled: true,
			Provider: "openrouter",
			Api: "chat",
			Model: "anthropic/claude-3.5-sonnet",
			ApiKey: "or-key",
			CustomHeaders: "HTTP-Referer: https://for-rest.dev; X-Title: For-Rest");

		AiSettings runtime = settings.ToRuntimeSettings();

		Assert.AreEqual(2, runtime.Provider.CustomHeaders.Count);
		Assert.AreEqual("https://for-rest.dev", runtime.Provider.CustomHeaders["HTTP-Referer"]);
		Assert.AreEqual("For-Rest", runtime.Provider.CustomHeaders["X-Title"]);
	}

	[TestMethod]
	public void ToRuntimeSettings_parses_custom_headers_from_newline_separated_string()
	{
		ForRestAiSettings settings = new(
			Enabled: true,
			Provider: "anthropic",
			Api: "chat",
			Model: "claude-3-5-sonnet-latest",
			ApiKey: "ak-key",
			CustomHeaders: "anthropic-version: 2023-06-01\nX-Source: for-rest");

		AiSettings runtime = settings.ToRuntimeSettings();

		Assert.AreEqual(2, runtime.Provider.CustomHeaders.Count);
		Assert.AreEqual("2023-06-01", runtime.Provider.CustomHeaders["anthropic-version"]);
		Assert.AreEqual("for-rest", runtime.Provider.CustomHeaders["X-Source"]);
	}

	[TestMethod]
	public void ToRuntimeSettings_custom_headers_are_case_insensitive()
	{
		ForRestAiSettings settings = new(
			Enabled: true,
			Provider: "custom",
			Api: "chat",
			Endpoint: "https://example.com/v1",
			Model: "m",
			ApiKey: "k",
			CustomHeaders: "X-Api-Key: one");

		AiSettings runtime = settings.ToRuntimeSettings();

		Assert.IsTrue(runtime.Provider.CustomHeaders.ContainsKey("x-api-key"));
	}

	[TestMethod]
	public void GetCurrentSettings_reads_ai_settings_from_config_file()
	{
		using TestConfigScope scope = new();
		File.WriteAllText(
			scope.ConfigFilePath,
			"""
			[appearance.theme]
			light = true
			azure = false
			dark = false
			black = false
			amber = false

			[ai]
			enabled = true
			stream_responses = false
			provider = "openai"
			api = "responses"
			model = "gpt-4.1-mini"
			api_key = "workbench-api-key"
			system_prompt = "Use terse answers."
			""");

		IWorkbenchAiSettingsProvider provider = new WorkbenchAiSettingsProvider(new ThemeConfigStore(), new ThemeConfigParser());

		AiSettings settings = provider.GetCurrentSettings();

		Assert.IsTrue(settings.Enabled);
		Assert.AreEqual(AiProviderKind.OpenAI, settings.Provider.ProviderKind);
		Assert.AreEqual(AiConversationTransport.Responses, settings.Provider.Transport);
		Assert.AreEqual("gpt-4.1-mini", settings.Provider.Model);
		Assert.AreEqual("workbench-api-key", settings.ApiKey.Value);
		Assert.IsFalse(settings.Conversation.StreamResponses);
		Assert.AreEqual("Use terse answers.", settings.SystemPromptPrefix);
	}

	private sealed class TestConfigScope : IDisposable
	{
		private readonly string _previousValue;
		private readonly string _rootPath;

		public TestConfigScope()
		{
			_previousValue = Environment.GetEnvironmentVariable("FORREST_CONFIG_FILE") ?? string.Empty;
			_rootPath = Path.Combine(Path.GetTempPath(), "ForRest-AI-Settings-Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_rootPath);
			ConfigFilePath = Path.Combine(_rootPath, "settings.toml");
			Environment.SetEnvironmentVariable("FORREST_CONFIG_FILE", ConfigFilePath);
		}

		public string ConfigFilePath { get; }

		public void Dispose()
		{
			Environment.SetEnvironmentVariable("FORREST_CONFIG_FILE", string.IsNullOrWhiteSpace(_previousValue) ? null : _previousValue);
			if (Directory.Exists(_rootPath))
			{
				Directory.Delete(_rootPath, recursive: true);
			}
		}
	}
}
