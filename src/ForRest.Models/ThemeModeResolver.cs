namespace ForRest.Models;

public enum ThemeVisualMode
{
    Light,
    Dark,
}

public static class ThemeModeResolver
{
    public static ThemeVisualMode Resolve(ThemeKind theme, bool isSystemDarkTheme)
    {
        return theme switch
        {
            ThemeKind.System => isSystemDarkTheme ? ThemeVisualMode.Dark : ThemeVisualMode.Light,
            ThemeKind.Light or ThemeKind.Azure => ThemeVisualMode.Light,
            _ => ThemeVisualMode.Dark,
        };
    }
}
