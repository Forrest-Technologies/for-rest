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
	public void GetCurrentSettings_reads_ai_settings_from_config_file()
	{
		using TestConfigScope scope = new();
		File.WriteAllText(
			scope.ConfigFilePath,
			"""
			license = ""

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
