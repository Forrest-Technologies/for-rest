namespace ForRest.Tests.Models;

[TestClass]
public sealed class ThemeModeResolverTests
{
    [TestMethod]
    public void Resolve_returns_system_light_when_windows_is_light()
    {
        var result = ThemeModeResolver.Resolve(ThemeKind.System, isSystemDarkTheme: false);

        Assert.AreEqual(ThemeVisualMode.Light, result);
    }

    [TestMethod]
    public void Resolve_returns_system_dark_when_windows_is_dark()
    {
        var result = ThemeModeResolver.Resolve(ThemeKind.System, isSystemDarkTheme: true);

        Assert.AreEqual(ThemeVisualMode.Dark, result);
    }

    [TestMethod]
    public void Resolve_treats_light_and_azure_as_light_modes()
    {
        Assert.AreEqual(ThemeVisualMode.Light, ThemeModeResolver.Resolve(ThemeKind.Light, isSystemDarkTheme: true));
        Assert.AreEqual(ThemeVisualMode.Light, ThemeModeResolver.Resolve(ThemeKind.Azure, isSystemDarkTheme: true));
    }

    [TestMethod]
    public void Resolve_treats_dark_variants_as_dark_modes()
    {
        Assert.AreEqual(ThemeVisualMode.Dark, ThemeModeResolver.Resolve(ThemeKind.Dark, isSystemDarkTheme: false));
        Assert.AreEqual(ThemeVisualMode.Dark, ThemeModeResolver.Resolve(ThemeKind.Black, isSystemDarkTheme: false));
        Assert.AreEqual(ThemeVisualMode.Dark, ThemeModeResolver.Resolve(ThemeKind.AmberDark, isSystemDarkTheme: false));
        Assert.AreEqual(ThemeVisualMode.Dark, ThemeModeResolver.Resolve(ThemeKind.Custom, isSystemDarkTheme: false));
    }
}
