using System.IO;
using ForRest.Maui.Services;
using ForRest.Maui.Theming;

namespace ForRest.Maui.Tests.Services;

[TestClass]
public sealed class WorkbenchMcpSettingsProviderTests
{
    [TestMethod]
    public void Parser_reads_full_mcp_section_values()
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

            [mcp]
            enabled = true
            bind_address = "0.0.0.0"
            port = 7500
            auth_token = "shared-secret"
            max_concurrent_sessions = 8
            """);

        Assert.IsTrue(document.Mcp.Enabled);
        Assert.AreEqual("0.0.0.0", document.Mcp.BindAddress);
        Assert.AreEqual(7500, document.Mcp.Port);
        Assert.AreEqual("shared-secret", document.Mcp.AuthToken);
        Assert.AreEqual(8, document.Mcp.MaxConcurrentSessions);
    }

    [TestMethod]
    public void Parser_ignores_invalid_port_and_keeps_default()
    {
        ThemeConfigParser parser = new();

        ThemeConfigDocument document = parser.Parse(
            """
            [mcp]
            enabled = true
            bind_address = "127.0.0.1"
            port = 99999
            """);

        Assert.AreEqual(7341, document.Mcp.Port);
    }

    [TestMethod]
    public void Template_round_trip_emits_and_reparses_mcp_section()
    {
        SettingsTomlTemplate template = new();
        ForRestSettings settings = new(ShellThemeName.Azure)
        {
            Mcp = new ForRestMcpSettings(
                Enabled: true,
                BindAddress: "127.0.0.1",
                Port: 7341,
                AuthToken: "round-trip",
                MaxConcurrentSessions: 2),
        };

        string text = template.Build(settings);

        StringAssert.Contains(text, "[mcp]");
        StringAssert.Contains(text, "enabled = true");
        StringAssert.Contains(text, "bind_address = \"127.0.0.1\"");
        StringAssert.Contains(text, "port = 7341");
        StringAssert.Contains(text, "auth_token = \"round-trip\"");
        StringAssert.Contains(text, "max_concurrent_sessions = 2");
        Assert.IsTrue(template.CanAutoSave(text), "regenerated config must round-trip through CanAutoSave");
    }

    [TestMethod]
    public void GetCurrentSettings_returns_defaults_when_mcp_section_is_missing()
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

        IWorkbenchMcpSettingsProvider provider = new WorkbenchMcpSettingsProvider(
            new ThemeConfigStore(),
            new ThemeConfigParser());

        ForRestMcpSettings settings = provider.GetCurrentSettings();

        Assert.IsFalse(settings.Enabled);
        Assert.AreEqual("127.0.0.1", settings.BindAddress);
        Assert.AreEqual(7341, settings.Port);
    }

    private sealed class TestConfigScope : IDisposable
    {
        private readonly string _previousValue;
        private readonly string _rootPath;

        public TestConfigScope()
        {
            _previousValue = Environment.GetEnvironmentVariable("FORREST_CONFIG_FILE") ?? string.Empty;
            _rootPath = Path.Combine(Path.GetTempPath(), "ForRest-MCP-Tests", Guid.NewGuid().ToString("N"));
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
