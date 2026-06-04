using ForRest.Maui.Theming;

namespace ForRest.Maui.Services;

/// <summary>
/// Reads the <c>[oauth]</c> section from the workbench settings store into a runtime DTO. The in-app
/// browser authorization broker reads <see cref="ForRestOAuthSettings.UseInternalBrowser"/> to decide
/// whether interactive OAuth sign-in runs inside the embedded browser pane or the system browser. Kept
/// platform-agnostic so every build can parse and display the setting.
/// </summary>
public interface IWorkbenchOAuthSettingsProvider
{
	ForRestOAuthSettings GetCurrentSettings();
}

public sealed class WorkbenchOAuthSettingsProvider : IWorkbenchOAuthSettingsProvider
{
	#region Private Fields

	private readonly ThemeConfigStore themeConfigStore;
	private readonly ThemeConfigParser themeConfigParser;

	#endregion

	#region Constructors

	public WorkbenchOAuthSettingsProvider(ThemeConfigStore themeConfigStore, ThemeConfigParser themeConfigParser)
	{
		this.themeConfigStore = themeConfigStore;
		this.themeConfigParser = themeConfigParser;
	}

	#endregion

	#region Public Methods

	public ForRestOAuthSettings GetCurrentSettings()
	{
		string rawText = themeConfigStore.ReadAllText();
		if (string.IsNullOrWhiteSpace(rawText))
		{
			return new ForRestOAuthSettings();
		}

		ThemeConfigDocument document = themeConfigParser.Parse(rawText);
		return document.OAuth;
	}

	#endregion
}
