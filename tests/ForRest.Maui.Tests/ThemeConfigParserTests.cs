using ForRest.Maui.Theming;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class ThemeConfigParserTests
{
	[TestMethod]
	public void Parse_reads_ai_section_values()
	{
		ThemeConfigParser parser = new();

		ThemeConfigDocument document = parser.Parse(
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
			provider = "azure_openai"
			api = "responses"
			endpoint = "https://example.test"
			model = "gpt-4o-mini"
			deployment_name = "gpt-4o-mini"
			api_key = "secret-api-key"
			system_prompt = "Be brief."
			""");

		Assert.AreEqual("super-secret-license", document.LicenseKey);
		Assert.IsTrue(document.Ai.Enabled);
		Assert.AreEqual("azure_openai", document.Ai.Provider);
		Assert.AreEqual("responses", document.Ai.Api);
		Assert.AreEqual("https://example.test", document.Ai.Endpoint);
		Assert.AreEqual("gpt-4o-mini", document.Ai.Model);
		Assert.AreEqual("gpt-4o-mini", document.Ai.DeploymentName);
		Assert.AreEqual("secret-api-key", document.Ai.ApiKey);
		Assert.AreEqual("Be brief.", document.Ai.SystemPrompt);
	}

	[TestMethod]
	public void Normalize_round_trips_ai_settings_and_theme_selection()
	{
		SettingsTomlTemplate template = new();
		ThemeConfigParser parser = new();
		ThemeConfigNormalizer normalizer = new(template);

		ThemeConfigDocument document = parser.Parse(
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
			api = "chat"
			endpoint = "https://api.example.test"
			model = "gpt-4o-mini"
			deployment_name = "gpt-4o-mini"
			api_key = "secret-api-key"
			system_prompt = "Be brief."
			""");

		ThemeNormalizationResult result = normalizer.Normalize(document);

		Assert.AreEqual(ShellThemeName.Azure, result.Settings.Theme);
		Assert.IsTrue(result.Settings.Ai.Enabled);
		Assert.AreEqual("secret-api-key", result.Settings.Ai.ApiKey);
		StringAssert.Contains(result.NormalizedText, "[ai]");
		StringAssert.Contains(result.NormalizedText, "api_key = \"secret-api-key\"");
	}
}
