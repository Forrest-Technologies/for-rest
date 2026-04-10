using Microsoft.Maui.Graphics;
using ForRest.Licensing;

namespace ForRest.Maui.Theming;

public enum ShellThemeName
{
	Light,
	Azure,
	Dark,
	Black,
	Amber
}

public sealed record ForRestAiSettings(
	bool Enabled = false,
	string Provider = "openai",
	string Api = "responses",
	string Endpoint = "",
	string Model = "",
	string DeploymentName = "",
	string ApiKey = "",
	string SystemPrompt = "",
	bool StreamResponses = true,
	string CustomHeaders = "")
{
	public bool HasConfiguredValues =>
		Enabled ||
		!string.Equals(Provider, "openai", StringComparison.OrdinalIgnoreCase) ||
		!string.Equals(Api, "responses", StringComparison.OrdinalIgnoreCase) ||
		!string.IsNullOrWhiteSpace(Endpoint) ||
		!string.IsNullOrWhiteSpace(Model) ||
		!string.IsNullOrWhiteSpace(DeploymentName) ||
		!string.IsNullOrWhiteSpace(ApiKey) ||
		!string.IsNullOrWhiteSpace(SystemPrompt) ||
		!string.IsNullOrWhiteSpace(CustomHeaders) ||
		!StreamResponses;
}

public sealed record ForRestMcpSettings(
	bool Enabled = false,
	string BindAddress = "127.0.0.1",
	int Port = 7341,
	string AuthToken = "",
	int MaxConcurrentSessions = 4)
{
	public bool HasConfiguredValues =>
		Enabled ||
		!string.Equals(BindAddress, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
		Port != 7341 ||
		!string.IsNullOrWhiteSpace(AuthToken) ||
		MaxConcurrentSessions != 4;
}

public sealed record ForRestStyleSettings(
	double EditorFontSize = 13.5d,
	double ResultPaneTabFontSize = 11.5d)
{
	public const double DefaultEditorFontSize = 13.5d;
	public const double MinEditorFontSize = 10d;
	public const double MaxEditorFontSize = 28d;
	public const double DefaultResultPaneTabFontSize = 11.5d;
	public const double MinResultPaneTabFontSize = 9d;
	public const double MaxResultPaneTabFontSize = 18d;

	public ForRestStyleSettings Normalize()
	{
		return this with
		{
			EditorFontSize = Normalize(EditorFontSize, DefaultEditorFontSize, MinEditorFontSize, MaxEditorFontSize),
			ResultPaneTabFontSize = Normalize(ResultPaneTabFontSize, DefaultResultPaneTabFontSize, MinResultPaneTabFontSize, MaxResultPaneTabFontSize)
		};
	}

	private static double Normalize(double value, double fallback, double min, double max)
	{
		if (!double.IsFinite(value))
		{
			return fallback;
		}

		return Math.Clamp(value, min, max);
	}
}

public sealed record ForRestSettings(
	ShellThemeName Theme,
	string LicenseKey = "")
{
	public ForRestStyleSettings Style { get; init; } = new();

	public ForRestAiSettings Ai { get; init; } = new();

	public ForRestMcpSettings Mcp { get; init; } = new();
}

public sealed record SettingsTomlLine(
	int LineNumber,
	string RawText,
	string? SectionName,
	bool IsBlank,
	bool IsComment,
	bool IsSectionHeader,
	bool IsKeyValue,
	string? Key,
	string? Value);

public sealed record ThemeConfigEntry(
	int LineNumber,
	string RawName,
	bool? SelectedValue,
	bool IsKnown);

public sealed record ThemeConfigDocument(
	IReadOnlyList<SettingsTomlLine> Lines,
	IReadOnlyList<ThemeConfigEntry> Entries,
	string LicenseKey,
	ForRestStyleSettings Style,
	ForRestAiSettings Ai,
	ForRestMcpSettings Mcp,
	IReadOnlyList<string> Messages);

public sealed record ThemeNormalizationResult(
	ForRestSettings Settings,
	string NormalizedText,
	IReadOnlyList<string> Messages);

public sealed record ActivationSnapshot(
	LicenseAccessStatus State,
	string StatusText,
	string DetailText,
	bool CanExecuteRequests,
	string? RegisteredTo = null,
	string? RegisteredEmail = null,
	DateTimeOffset? ServerValidatedUtc = null,
	DateTimeOffset? LeaseRefreshAfterUtc = null,
	DateTimeOffset? LeaseExpiresUtc = null,
	DateTimeOffset? LicenseExpiresUtc = null,
	DateTimeOffset? BuildGraceExpiresUtc = null);

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
	string AccentTextColor,
	string SelectionSoftColor,
	string SelectionStrongColor,
	string SelectionBorderColor,
	string SelectionTextColor,
	string EditorBackgroundColor,
	string EditorGutterColor,
	string SplitterColor,
	string OverlayBackdropColor,
	string StatusBackgroundColor,
	string StatusTextColor,
	string StatusBorderColor,
	string SuccessColor,
	string WarningColor,
	string DangerColor,
	string MethodGetColor,
	string MethodPostColor,
	string MethodPutColor,
	string MethodDeleteColor,
	string MethodNeutralColor,
	string WindowChromeBackgroundColor,
	string WindowChromeForegroundColor,
	string WindowChromeInactiveBackgroundColor,
	string WindowChromeInactiveForegroundColor);

public sealed record ShellThemeDefinition(
	ShellThemeName Name,
	bool IsDark,
	string MonacoThemeKey,
	ThemeColorTokens Colors);

public sealed class ThemeChangedEventArgs(
	ShellThemeDefinition theme,
	ForRestSettings settings,
	string statusMessage,
	bool configNormalized,
	bool isPreview = false) : EventArgs
{
	public ShellThemeDefinition Theme { get; } = theme;

	public ForRestSettings Settings { get; } = settings;

	public string StatusMessage { get; } = statusMessage;

	public bool ConfigNormalized { get; } = configNormalized;

	public bool IsPreview { get; } = isPreview;
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
	public const string AccentTextColor = "ThemeAccentTextColor";
	public const string SelectionSoftColor = "ThemeSelectionSoftColor";
	public const string SelectionStrongColor = "ThemeSelectionStrongColor";
	public const string SelectionBorderColor = "ThemeSelectionBorderColor";
	public const string SelectionTextColor = "ThemeSelectionTextColor";
	public const string EditorBackgroundColor = "ThemeEditorBackgroundColor";
	public const string EditorGutterColor = "ThemeEditorGutterColor";
	public const string SplitterColor = "ThemeSplitterColor";
	public const string OverlayBackdropColor = "ThemeOverlayBackdropColor";
	public const string StatusBackgroundColor = "ThemeStatusBackgroundColor";
	public const string StatusTextColor = "ThemeStatusTextColor";
	public const string StatusBorderColor = "ThemeStatusBorderColor";
	public const string SuccessColor = "ThemeSuccessColor";
	public const string WarningColor = "ThemeWarningColor";
	public const string DangerColor = "ThemeDangerColor";
	public const string MethodGetColor = "ThemeMethodGetColor";
	public const string MethodPostColor = "ThemeMethodPostColor";
	public const string MethodPutColor = "ThemeMethodPutColor";
	public const string MethodDeleteColor = "ThemeMethodDeleteColor";
	public const string MethodNeutralColor = "ThemeMethodNeutralColor";
	public const string WindowChromeBackgroundColor = "ThemeWindowChromeBackgroundColor";
	public const string WindowChromeForegroundColor = "ThemeWindowChromeForegroundColor";
	public const string WindowChromeInactiveBackgroundColor = "ThemeWindowChromeInactiveBackgroundColor";
	public const string WindowChromeInactiveForegroundColor = "ThemeWindowChromeInactiveForegroundColor";
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
