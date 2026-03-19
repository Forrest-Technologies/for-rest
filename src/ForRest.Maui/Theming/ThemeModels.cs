using Microsoft.Maui.Graphics;

namespace ForRest.Maui.Theming;

public enum ShellThemeName
{
	Light,
	Azure,
	Dark,
	Black,
	Amber
}

public sealed record ForRestSettings(ShellThemeName Theme);

public sealed record ThemeConfigEntry(string RawName, bool? SelectedValue, bool IsKnown);

public sealed record ThemeConfigDocument(
	IReadOnlyList<ThemeConfigEntry> Entries,
	IReadOnlyList<string> Messages);

public sealed record ThemeNormalizationResult(
	ForRestSettings Settings,
	string NormalizedText,
	IReadOnlyList<string> Messages);

public sealed record EditorEditableRange(
	int StartLineNumber,
	int StartColumn,
	int EndLineNumber,
	int EndColumn);

public sealed record ThemeColorTokens(
	string CanvasColor,
	string SurfaceColor,
	string SurfaceRaisedColor,
	string SurfaceMutedColor,
	string BorderColor,
	string DividerColor,
	string TextPrimaryColor,
	string TextSecondaryColor,
	string TextMutedColor,
	string AccentColor,
	string AccentSoftColor,
	string AccentStrongColor,
	string EditorBackgroundColor,
	string EditorGutterColor,
	string SplitterColor,
	string StatusBackgroundColor,
	string StatusTextColor,
	string StatusBorderColor);

public sealed record ShellThemeDefinition(
	ShellThemeName Name,
	bool IsDark,
	string MonacoThemeKey,
	ThemeColorTokens Colors);

public sealed class ThemeChangedEventArgs(
	ShellThemeDefinition theme,
	string statusMessage,
	bool configNormalized) : EventArgs
{
	public ShellThemeDefinition Theme { get; } = theme;

	public string StatusMessage { get; } = statusMessage;

	public bool ConfigNormalized { get; } = configNormalized;
}

public static class ThemeResourceKeys
{
	public const string CanvasColor = "ThemeCanvasColor";
	public const string SurfaceColor = "ThemeSurfaceColor";
	public const string SurfaceRaisedColor = "ThemeSurfaceRaisedColor";
	public const string SurfaceMutedColor = "ThemeSurfaceMutedColor";
	public const string BorderColor = "ThemeBorderColor";
	public const string DividerColor = "ThemeDividerColor";
	public const string TextPrimaryColor = "ThemeTextPrimaryColor";
	public const string TextSecondaryColor = "ThemeTextSecondaryColor";
	public const string TextMutedColor = "ThemeTextMutedColor";
	public const string AccentColor = "ThemeAccentColor";
	public const string AccentSoftColor = "ThemeAccentSoftColor";
	public const string AccentStrongColor = "ThemeAccentStrongColor";
	public const string EditorBackgroundColor = "ThemeEditorBackgroundColor";
	public const string EditorGutterColor = "ThemeEditorGutterColor";
	public const string SplitterColor = "ThemeSplitterColor";
	public const string StatusBackgroundColor = "ThemeStatusBackgroundColor";
	public const string StatusTextColor = "ThemeStatusTextColor";
	public const string StatusBorderColor = "ThemeStatusBorderColor";
}

public static class ThemeSupport
{
	private static readonly IReadOnlyList<ShellThemeName> SupportedThemes =
	[
		ShellThemeName.Light,
		ShellThemeName.Azure,
		ShellThemeName.Dark,
		ShellThemeName.Black,
		ShellThemeName.Amber
	];

	public static IReadOnlyList<ShellThemeName> OrderedThemes => SupportedThemes;

	public static bool TryParseThemeName(string rawName, out ShellThemeName theme)
	{
		return Enum.TryParse(rawName.Trim(), ignoreCase: true, out theme) && SupportedThemes.Contains(theme);
	}

	public static string ToConfigName(this ShellThemeName theme)
	{
		return theme.ToString().ToLowerInvariant();
	}

	public static Color ToColor(string hex)
	{
		return Color.FromArgb(hex);
	}
}
