using System;
using System.IO;
using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using ForRest.Licensing;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class SettingsTomlDocumentServiceTests
{
	[TestMethod]
	public void LoadOrCreate_masks_existing_license_value()
	{
		using TestConfigScope scope = new();
		SettingsTomlDocumentService service = CreateService();
		File.WriteAllText(
			scope.ConfigFilePath,
			"""
			license = "super-secret-license"

			[appearance.theme]
			light = false
			azure = true
			dark = false
			black = false
			amber = false
			""");

		string editorText = service.LoadOrCreate(new ForRestSettings(ShellThemeName.Azure));

		Assert.IsFalse(editorText.Contains("super-secret-license", StringComparison.Ordinal));
		StringAssert.Contains(editorText, $"license = \"{SettingsTomlTemplate.MaskedLicenseValue}\"");
		StringAssert.Contains(editorText, "[license.info]");
		StringAssert.Contains(editorText, "build_grace_ends_utc = ");
	}

	[TestMethod]
	public void LoadOrCreate_masks_existing_ai_api_key_value()
	{
		using TestConfigScope scope = new();
		SettingsTomlDocumentService service = CreateService();
		File.WriteAllText(
			scope.ConfigFilePath,
			"""
			license = "super-secret-license"

			[appearance.theme]
			light = false
			azure = true
			dark = false
			black = false
			amber = false

			[ai]
			enabled = true
			provider = "openai"
			api = "responses"
			endpoint = "https://api.example.test"
			model = "gpt-4o-mini"
			deployment_name = "gpt-4o-mini"
			api_key = "secret-api-key"
			system_prompt = "Be brief."
			""");

		string editorText = service.LoadOrCreate(new ForRestSettings(ShellThemeName.Azure));

		Assert.IsFalse(editorText.Contains("secret-api-key", StringComparison.Ordinal));
		StringAssert.Contains(editorText, $"api_key = \"{SettingsTomlTemplate.MaskedLicenseValue}\"");
		StringAssert.Contains(editorText, "enabled = true");
	}

	[TestMethod]
	public void LoadOrCreate_adds_ai_section_to_legacy_settings()
	{
		using TestConfigScope scope = new();
		SettingsTomlDocumentService service = CreateService();
		File.WriteAllText(
			scope.ConfigFilePath,
			"""
			license = "super-secret-license"

			[appearance.theme]
			light = false
			azure = true
			dark = false
			black = false
			amber = false
			""");

		string editorText = service.LoadOrCreate(new ForRestSettings(ShellThemeName.Azure));

		StringAssert.Contains(editorText, "[ai]");
		StringAssert.Contains(editorText, "[appearance.style]");
		StringAssert.Contains(editorText, "editor_font_size = 13.5");
		StringAssert.Contains(editorText, "enabled = false");
		StringAssert.Contains(editorText, "stream_responses = true");
		Assert.IsFalse(editorText.Contains("api_key = ", StringComparison.Ordinal));
	}

	[TestMethod]
	public void LoadOrCreate_describes_openai_endpoint_as_optional()
	{
		SettingsTomlTemplate template = new();
		string editorText = template.Build(
			new ForRestSettings(ShellThemeName.Azure)
			{
				Ai = new ForRestAiSettings(Enabled: true)
			});

		StringAssert.Contains(editorText, "provider accepts openai, azure_openai, grok");
		StringAssert.Contains(editorText, "Most endpoints are optional");
		StringAssert.Contains(editorText, "Azure OpenAI requires endpoint and deployment_name");
	}

	[TestMethod]
	public void CanAutoSave_rejects_unterminated_ai_string_values()
	{
		SettingsTomlTemplate template = new();
		string text =
			"""
			license = ""

			[appearance.theme]
			light = false
			azure = true
			dark = false
			black = false
			amber = false

			[appearance.style]
			editor_font_size = 13.5
			result_pane_tab_font_size = 11.5

			[ai]
			enabled = true
			stream_responses = true
			provider = "openai"
			api = "responses"
			endpoint = "https://api.example.test"
			model = "gpt-5.4-na
			deployment_name = "gpt-5.4-nano"
			api_key = ""
			system_prompt = ""
			""";

		Assert.IsFalse(template.CanAutoSave(text));
	}

	[TestMethod]
	public void SaveRawText_persists_style_section_values()
	{
		using TestConfigScope scope = new();
		SettingsTomlDocumentService service = CreateService();
		File.WriteAllText(
			scope.ConfigFilePath,
			"""
			license = ""

			[appearance.theme]
			light = false
			azure = true
			dark = false
			black = false
			amber = false

			[appearance.style]
			editor_font_size = 13.5
			result_pane_tab_font_size = 11.5
			""");

		string editorText = service.LoadOrCreate(new ForRestSettings(ShellThemeName.Azure));
		string updatedText = editorText
			.Replace("editor_font_size = 13.5", "editor_font_size = 17.25", StringComparison.Ordinal)
			.Replace("result_pane_tab_font_size = 11.5", "result_pane_tab_font_size = 13", StringComparison.Ordinal);
		service.SaveRawText(updatedText);

		string rawText = File.ReadAllText(scope.ConfigFilePath);
		StringAssert.Contains(rawText, "editor_font_size = 17.25");
		StringAssert.Contains(rawText, "result_pane_tab_font_size = 13");
	}

	[TestMethod]
	public void SaveRawText_preserves_hidden_license_when_mask_is_unchanged()
	{
		using TestConfigScope scope = new();
		SettingsTomlDocumentService service = CreateService();
		File.WriteAllText(
			scope.ConfigFilePath,
			"""
			license = "super-secret-license"

			[appearance.theme]
			light = false
			azure = true
			dark = false
			black = false
			amber = false

			[ai]
			enabled = true
			provider = "openai"
			api = "responses"
			endpoint = "https://api.example.test"
			model = "gpt-4o-mini"
			deployment_name = "gpt-4o-mini"
			api_key = "secret-api-key"
			system_prompt = "Be brief."
			""");

		string editorText = service.LoadOrCreate(new ForRestSettings(ShellThemeName.Azure));
		string edited = editorText
			.Replace("azure = true", "azure = false", StringComparison.Ordinal)
			.Replace("light = false", "light = true", StringComparison.Ordinal)
			.Replace("model = \"gpt-4o-mini\"", "model = \"gpt-4.1-mini\"", StringComparison.Ordinal);
		service.SaveRawText(edited);

		string rawText = File.ReadAllText(scope.ConfigFilePath);
		StringAssert.Contains(rawText, "license = \"super-secret-license\"");
		StringAssert.Contains(rawText, "api_key = \"secret-api-key\"");
		StringAssert.Contains(rawText, "light = true");
		StringAssert.Contains(rawText, "model = \"gpt-4.1-mini\"");
	}

	[TestMethod]
	public void SaveRawText_replaces_license_when_user_edits_value()
	{
		using TestConfigScope scope = new();
		SettingsTomlDocumentService service = CreateService();
		File.WriteAllText(
			scope.ConfigFilePath,
			"""
			license = "super-secret-license"

			[appearance.theme]
			light = false
			azure = true
			dark = false
			black = false
			amber = false

			[ai]
			enabled = true
			provider = "openai"
			api = "responses"
			endpoint = "https://api.example.test"
			model = "gpt-4o-mini"
			deployment_name = "gpt-4o-mini"
			api_key = "secret-api-key"
			system_prompt = "Be brief."
			""");

		string editorText = service.LoadOrCreate(new ForRestSettings(ShellThemeName.Azure));
		service.SaveRawText(editorText.Replace(SettingsTomlTemplate.MaskedLicenseValue, "replacement-license", StringComparison.Ordinal));

		string rawText = File.ReadAllText(scope.ConfigFilePath);
		StringAssert.Contains(rawText, "license = \"replacement-license\"");
		Assert.IsFalse(rawText.Contains(SettingsTomlTemplate.MaskedLicenseValue, StringComparison.Ordinal));
	}

	[TestMethod]
	public void SaveRawText_replaces_ai_api_key_when_user_edits_value()
	{
		using TestConfigScope scope = new();
		SettingsTomlDocumentService service = CreateService();
		File.WriteAllText(
			scope.ConfigFilePath,
			"""
			license = ""

			[appearance.theme]
			light = false
			azure = true
			dark = false
			black = false
			amber = false

			[ai]
			enabled = true
			provider = "openai"
			api = "responses"
			endpoint = "https://api.example.test"
			model = "gpt-4o-mini"
			deployment_name = "gpt-4o-mini"
			api_key = "secret-api-key"
			system_prompt = "Be brief."
			""");

		string editorText = service.LoadOrCreate(new ForRestSettings(ShellThemeName.Azure));
		service.SaveRawText(editorText.Replace(SettingsTomlTemplate.MaskedLicenseValue, "replacement-api-key", StringComparison.Ordinal));

		string rawText = File.ReadAllText(scope.ConfigFilePath);
		StringAssert.Contains(rawText, "api_key = \"replacement-api-key\"");
		Assert.IsFalse(rawText.Contains(SettingsTomlTemplate.MaskedLicenseValue, StringComparison.Ordinal));
	}

	[TestMethod]
	public void SaveRawText_ignores_edits_to_generated_license_info_block()
	{
		using TestConfigScope scope = new();
		SettingsTomlDocumentService service = CreateService();
		File.WriteAllText(
			scope.ConfigFilePath,
			"""
			license = ""

			[appearance.theme]
			light = false
			azure = true
			dark = false
			black = false
			amber = false
			""");

		string editorText = service.LoadOrCreate(new ForRestSettings(ShellThemeName.Azure));
		string edited = editorText
			.Replace("summary = ", "summary = \"hacked\" # ", StringComparison.Ordinal)
			.Replace("lease_expires_utc = ", "lease_expires_utc = \"2099-01-01T00:00:00Z\" # ", StringComparison.Ordinal);
		service.SaveRawText(edited);

		string rawText = File.ReadAllText(scope.ConfigFilePath);
		Assert.IsFalse(rawText.Contains("[license.info]", StringComparison.Ordinal));
		Assert.IsFalse(rawText.Contains("hacked", StringComparison.Ordinal));
		Assert.IsFalse(rawText.Contains("999", StringComparison.Ordinal));
	}

	private static SettingsTomlDocumentService CreateService()
	{
		SettingsTomlTemplate template = new();
		ThemeConfigParser parser = new();
		ThemeConfigNormalizer normalizer = new(template);
		ThemeConfigStore store = new();
		return new SettingsTomlDocumentService(
			store,
			parser,
			normalizer,
			template);
	}

	private sealed class TestConfigScope : IDisposable
	{
		private readonly string _previousValue;
		private readonly string _rootPath;

		public TestConfigScope()
		{
			_previousValue = Environment.GetEnvironmentVariable("FORREST_CONFIG_FILE") ?? string.Empty;
			_rootPath = Path.Combine(Path.GetTempPath(), "ForRest-Maui-Tests", Guid.NewGuid().ToString("N"));
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
