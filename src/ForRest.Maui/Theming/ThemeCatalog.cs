namespace ForRest.Maui.Theming;

public sealed class ThemeCatalog
{
	private readonly IReadOnlyDictionary<ShellThemeName, ShellThemeDefinition> _themes =
		new Dictionary<ShellThemeName, ShellThemeDefinition>
		{
			[ShellThemeName.Light] = new(
				ShellThemeName.Light,
				false,
				"forrest-light",
				new ThemeColorTokens(
					CanvasColor: "#F4F5F7",
					SurfaceColor: "#FFFFFF",
					SurfaceRaisedColor: "#F8F9FB",
					SurfaceMutedColor: "#EDF1F4",
					BorderColor: "#D4DCE5",
					DividerColor: "#E4E8ED",
					TextPrimaryColor: "#16202A",
					TextSecondaryColor: "#5F6A78",
					TextMutedColor: "#7C8796",
					AccentColor: "#4E6D8B",
					AccentSoftColor: "#EAF0F5",
					AccentStrongColor: "#39546F",
					EditorBackgroundColor: "#FCFDFE",
					EditorGutterColor: "#F2F4F7",
					SplitterColor: "#CDD5DE",
					StatusBackgroundColor: "#F7F8FA",
					StatusTextColor: "#687483",
					StatusBorderColor: "#E1E6EB")),
			[ShellThemeName.Azure] = new(
				ShellThemeName.Azure,
				false,
				"forrest-azure",
				new ThemeColorTokens(
					CanvasColor: "#F3F5F7",
					SurfaceColor: "#FFFFFF",
					SurfaceRaisedColor: "#F8FAFB",
					SurfaceMutedColor: "#EEF2F5",
					BorderColor: "#D4DCE5",
					DividerColor: "#E4E8ED",
					TextPrimaryColor: "#16202A",
					TextSecondaryColor: "#596574",
					TextMutedColor: "#758091",
					AccentColor: "#1E6FB9",
					AccentSoftColor: "#E6F0FA",
					AccentStrongColor: "#114D85",
					EditorBackgroundColor: "#FBFCFD",
					EditorGutterColor: "#F4F7FA",
					SplitterColor: "#C8D0D9",
					StatusBackgroundColor: "#F4F7FA",
					StatusTextColor: "#647182",
					StatusBorderColor: "#DEE5EC")),
			[ShellThemeName.Dark] = new(
				ShellThemeName.Dark,
				true,
				"forrest-dark",
				new ThemeColorTokens(
					CanvasColor: "#161C24",
					SurfaceColor: "#1D2530",
					SurfaceRaisedColor: "#242D38",
					SurfaceMutedColor: "#212A34",
					BorderColor: "#364250",
					DividerColor: "#2D3741",
					TextPrimaryColor: "#EEF3F7",
					TextSecondaryColor: "#B0BBC6",
					TextMutedColor: "#8794A4",
					AccentColor: "#73B7F6",
					AccentSoftColor: "#173550",
					AccentStrongColor: "#B8DDFF",
					EditorBackgroundColor: "#141B24",
					EditorGutterColor: "#1A232D",
					SplitterColor: "#4A5868",
					StatusBackgroundColor: "#1A212B",
					StatusTextColor: "#AAB5C0",
					StatusBorderColor: "#2A3440")),
			[ShellThemeName.Black] = new(
				ShellThemeName.Black,
				true,
				"forrest-black",
				new ThemeColorTokens(
					CanvasColor: "#0F1216",
					SurfaceColor: "#15191E",
					SurfaceRaisedColor: "#181D23",
					SurfaceMutedColor: "#1A2026",
					BorderColor: "#2B343E",
					DividerColor: "#232B33",
					TextPrimaryColor: "#F3F6F8",
					TextSecondaryColor: "#B9C2CA",
					TextMutedColor: "#8B95A2",
					AccentColor: "#8FB4E5",
					AccentSoftColor: "#1D2731",
					AccentStrongColor: "#D9E7FA",
					EditorBackgroundColor: "#101419",
					EditorGutterColor: "#151B21",
					SplitterColor: "#3A4552",
					StatusBackgroundColor: "#12171C",
					StatusTextColor: "#B1BBC5",
					StatusBorderColor: "#232B33")),
			[ShellThemeName.Amber] = new(
				ShellThemeName.Amber,
				false,
				"forrest-amber",
				new ThemeColorTokens(
					CanvasColor: "#F8F4ED",
					SurfaceColor: "#FFFDF8",
					SurfaceRaisedColor: "#FBF6EE",
					SurfaceMutedColor: "#F4ECDF",
					BorderColor: "#DDCFBB",
					DividerColor: "#E7DAC7",
					TextPrimaryColor: "#2B2218",
					TextSecondaryColor: "#6D604F",
					TextMutedColor: "#8D7E6A",
					AccentColor: "#B36B1E",
					AccentSoftColor: "#F8E8D2",
					AccentStrongColor: "#82531F",
					EditorBackgroundColor: "#FFFCF6",
					EditorGutterColor: "#F5ECDD",
					SplitterColor: "#D2C2AA",
					StatusBackgroundColor: "#F6EEE2",
					StatusTextColor: "#716352",
					StatusBorderColor: "#E2D7C6"))
		};

	public ShellThemeDefinition GetTheme(ShellThemeName themeName)
	{
		return _themes[themeName];
	}
}
