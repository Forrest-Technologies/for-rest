using ForRest.Maui.Theming;

namespace ForRest.Maui.Services;

/// <summary>
/// Reads the `[mcp]` section from the workbench settings store into a
/// runtime DTO the MCP host service can consume. Kept platform-agnostic
/// so non-desktop builds can still parse / display the settings without
/// pulling a reference to the ForRest.Mcp project.
/// </summary>
public interface IWorkbenchMcpSettingsProvider
{
	ForRestMcpSettings GetCurrentSettings();
}

public sealed class WorkbenchMcpSettingsProvider : IWorkbenchMcpSettingsProvider
{
	private readonly ThemeConfigStore themeConfigStore;
	private readonly ThemeConfigParser themeConfigParser;

	public WorkbenchMcpSettingsProvider(ThemeConfigStore themeConfigStore, ThemeConfigParser themeConfigParser)
	{
		this.themeConfigStore = themeConfigStore;
		this.themeConfigParser = themeConfigParser;
	}

	public ForRestMcpSettings GetCurrentSettings()
	{
		string rawText = themeConfigStore.ReadAllText();
		if (string.IsNullOrWhiteSpace(rawText))
		{
			return new ForRestMcpSettings();
		}

		ThemeConfigDocument document = themeConfigParser.Parse(rawText);
		return document.Mcp;
	}
}
