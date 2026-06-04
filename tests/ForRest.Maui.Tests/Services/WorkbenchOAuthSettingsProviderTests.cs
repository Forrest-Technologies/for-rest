using System.IO;
using ForRest.Maui.Services;
using ForRest.Maui.Theming;

namespace ForRest.Maui.Tests.Services;

[TestClass]
public sealed class WorkbenchOAuthSettingsProviderTests
{
    [TestMethod]
    public void Parser_reads_oauth_use_internal_browser_true()
    {
        ThemeConfigParser parser = new();

        ThemeConfigDocument document = parser.Parse(
            """
            license = ""

            [appearance.theme]
            light = true
            azure = false
            dark = false
            black = false
            amber = false

            [appearance.style]
            editor_font_size = 13.5
            result_pane_tab_font_size = 11.5

            [oauth]
            use_internal_browser = true
            """);

        Assert.IsTrue(document.OAuth.UseInternalBrowser);
    }

    [TestMethod]
    public void Parser_defaults_oauth_use_internal_browser_to_false()
    {
        ThemeConfigParser parser = new();

        ThemeConfigDocument document = parser.Parse(
            """
            [appearance.theme]
            light = true
            azure = false
            dark = false
            black = false
            amber = false
            """);

        Assert.IsFalse(document.OAuth.UseInternalBrowser);
    }

    [TestMethod]
    public void Parser_ignores_invalid_oauth_value_and_keeps_default()
    {
        ThemeConfigParser parser = new();

        ThemeConfigDocument document = parser.Parse(
            """
            [oauth]
            use_internal_browser = maybe
            """);

        Assert.IsFalse(document.OAuth.UseInternalBrowser);
        Assert.IsTrue(document.Messages.Any(message => message.Contains("use_internal_browser")));
    }

    [TestMethod]
    public void Template_round_trip_emits_and_reparses_oauth_section()
    {
        SettingsTomlTemplate template = new();
        ForRestSettings settings = new(ShellThemeName.Azure)
        {
            OAuth = new ForRestOAuthSettings(UseInternalBrowser: true),
        };

        string text = template.Build(settings);

        StringAssert.Contains(text, "[oauth]");
        StringAssert.Contains(text, "use_internal_browser = true");
        Assert.IsTrue(template.CanAutoSave(text), "regenerated config must round-trip through CanAutoSave");

        ThemeConfigDocument document = new ThemeConfigParser().Parse(text);
        Assert.IsTrue(document.OAuth.UseInternalBrowser);
    }

    [TestMethod]
    public void Normalizer_preserves_existing_oauth_section()
    {
        ThemeConfigParser parser = new();
        SettingsTomlTemplate template = new();
        ThemeConfigNormalizer normalizer = new(template);

        ThemeConfigDocument document = parser.Parse(
            """
            license = ""

            [appearance.theme]
            light = true
            azure = false
            dark = false
            black = false
            amber = false

            [appearance.style]
            editor_font_size = 13.5
            result_pane_tab_font_size = 11.5

            [oauth]
            use_internal_browser = true
            """);

        ThemeNormalizationResult result = normalizer.Normalize(document);

        Assert.IsTrue(result.Settings.OAuth.UseInternalBrowser);
        StringAssert.Contains(result.NormalizedText, "use_internal_browser = true");

        // The normalized text must itself re-parse with the flag intact.
        Assert.IsTrue(parser.Parse(result.NormalizedText).OAuth.UseInternalBrowser);
    }

    [TestMethod]
    public void GetCurrentSettings_returns_defaults_when_oauth_section_is_missing()
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
            """);

        IWorkbenchOAuthSettingsProvider provider = new WorkbenchOAuthSettingsProvider(
            new ThemeConfigStore(),
            new ThemeConfigParser());

        ForRestOAuthSettings settings = provider.GetCurrentSettings();

        Assert.IsFalse(settings.UseInternalBrowser);
    }

    private sealed class TestConfigScope : IDisposable
    {
        private readonly string _previousValue;
        private readonly string _rootPath;

        public TestConfigScope()
        {
            _previousValue = Environment.GetEnvironmentVariable("FORREST_CONFIG_FILE") ?? string.Empty;
            _rootPath = Path.Combine(Path.GetTempPath(), "ForRest-OAuth-Tests", Guid.NewGuid().ToString("N"));
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
