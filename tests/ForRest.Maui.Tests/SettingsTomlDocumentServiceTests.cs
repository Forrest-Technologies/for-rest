using System;
using System.IO;
using ForRest.Maui.Theming;

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
			""");

		string editorText = service.LoadOrCreate(new ForRestSettings(ShellThemeName.Azure));
		service.SaveRawText(editorText.Replace("azure = true", "azure = false", StringComparison.Ordinal).Replace("light = false", "light = true", StringComparison.Ordinal));

		string rawText = File.ReadAllText(scope.ConfigFilePath);
		StringAssert.Contains(rawText, "license = \"super-secret-license\"");
		StringAssert.Contains(rawText, "light = true");
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
			""");

		string editorText = service.LoadOrCreate(new ForRestSettings(ShellThemeName.Azure));
		service.SaveRawText(editorText.Replace(SettingsTomlTemplate.MaskedLicenseValue, "replacement-license", StringComparison.Ordinal));

		string rawText = File.ReadAllText(scope.ConfigFilePath);
		StringAssert.Contains(rawText, "license = \"replacement-license\"");
		Assert.IsFalse(rawText.Contains(SettingsTomlTemplate.MaskedLicenseValue, StringComparison.Ordinal));
	}

	private static SettingsTomlDocumentService CreateService()
	{
		SettingsTomlTemplate template = new();
		ThemeConfigParser parser = new();
		ThemeConfigNormalizer normalizer = new(template);
		ThemeConfigStore store = new();
		return new SettingsTomlDocumentService(store, parser, normalizer, template);
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
