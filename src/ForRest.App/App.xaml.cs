using ForRest.App.ViewModels;
using ForRest.Infrastructure.Sqlite;
using ForRest.Plugins.Host;
using ForRest.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ForRest.App;

public partial class App : Application
{
    #region Private Fields

    private readonly IHost host;
    private readonly UISettings uiSettings = new();
    private ThemeKind activeTheme = ThemeKind.System;

    private MainWindow? window;

    #endregion

    #region Constructors

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        uiSettings.ColorValuesChanged += OnColorValuesChanged;

        host = Host.CreateDefaultBuilder()
            .ConfigureLogging(
                logging =>
                {
                    logging.ClearProviders();
                    logging.AddDebug();
                })
            .ConfigureServices(
                services =>
                {
                    services.AddForRestSqlite();
                    services.AddForRestCore();
                    services.AddSingleton<PluginCatalog>();
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddSingleton<MainWindow>();
                })
            .Build();

        host.Start();
    }

    #endregion

    #region Public Methods

    public void ApplyTheme(ThemeKind theme)
    {
        activeTheme = theme;

        var visualMode = ThemeModeResolver.Resolve(theme, IsSystemDarkTheme());
        var isLightTheme = visualMode == ThemeVisualMode.Light;

        if (window?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = isLightTheme ? ElementTheme.Light : ElementTheme.Dark;
        }

        var palette = theme switch
        {
            ThemeKind.Light => CreateLightPalette(accent: Color.FromArgb(255, 54, 119, 255)),
            ThemeKind.Azure => CreateLightPalette(accent: Color.FromArgb(255, 0, 120, 212)),
            ThemeKind.Black => CreateDarkPalette(
                background: Color.FromArgb(255, 10, 10, 12),
                chrome: Color.FromArgb(255, 12, 12, 15),
                panel: Color.FromArgb(255, 18, 20, 24),
                panelAlt: Color.FromArgb(255, 15, 17, 21),
                raised: Color.FromArgb(255, 22, 25, 31),
                codeSurface: Color.FromArgb(255, 9, 10, 13),
                input: Color.FromArgb(255, 15, 17, 22),
                hairline: Color.FromArgb(255, 38, 42, 50),
                accent: Color.FromArgb(255, 91, 162, 255),
                selection: Color.FromArgb(255, 30, 36, 45),
                hover: Color.FromArgb(255, 21, 24, 29),
                text: Color.FromArgb(255, 245, 247, 250),
                mutedText: Color.FromArgb(255, 160, 167, 176),
                subtleText: Color.FromArgb(255, 122, 130, 142)),
            ThemeKind.AmberDark => CreateDarkPalette(
                background: Color.FromArgb(255, 26, 20, 14),
                chrome: Color.FromArgb(255, 22, 17, 13),
                panel: Color.FromArgb(255, 34, 27, 19),
                panelAlt: Color.FromArgb(255, 29, 23, 17),
                raised: Color.FromArgb(255, 41, 32, 23),
                codeSurface: Color.FromArgb(255, 20, 16, 12),
                input: Color.FromArgb(255, 29, 24, 18),
                hairline: Color.FromArgb(255, 80, 61, 37),
                accent: Color.FromArgb(255, 214, 137, 24),
                selection: Color.FromArgb(255, 52, 39, 27),
                hover: Color.FromArgb(255, 43, 33, 24),
                text: Color.FromArgb(255, 248, 242, 232),
                mutedText: Color.FromArgb(255, 193, 177, 153),
                subtleText: Color.FromArgb(255, 145, 129, 108)),
            ThemeKind.Custom => CreateDarkPalette(
                background: Color.FromArgb(255, 17, 21, 28),
                chrome: Color.FromArgb(255, 14, 18, 24),
                panel: Color.FromArgb(255, 24, 30, 40),
                panelAlt: Color.FromArgb(255, 20, 25, 34),
                raised: Color.FromArgb(255, 29, 35, 46),
                codeSurface: Color.FromArgb(255, 13, 17, 24),
                input: Color.FromArgb(255, 18, 23, 31),
                hairline: Color.FromArgb(255, 58, 65, 79),
                accent: Color.FromArgb(255, 112, 124, 255),
                selection: Color.FromArgb(255, 36, 44, 58),
                hover: Color.FromArgb(255, 29, 37, 49),
                text: Color.FromArgb(255, 243, 246, 250),
                mutedText: Color.FromArgb(255, 159, 168, 184),
                subtleText: Color.FromArgb(255, 123, 133, 149)),
            ThemeKind.System when isLightTheme => CreateLightPalette(accent: Color.FromArgb(255, 54, 119, 255)),
            _ => CreateDarkPalette(
                background: Color.FromArgb(255, 14, 17, 23),
                chrome: Color.FromArgb(255, 16, 21, 28),
                panel: Color.FromArgb(255, 20, 26, 34),
                panelAlt: Color.FromArgb(255, 16, 22, 29),
                raised: Color.FromArgb(255, 25, 32, 42),
                codeSurface: Color.FromArgb(255, 12, 17, 24),
                input: Color.FromArgb(255, 17, 24, 33),
                hairline: Color.FromArgb(255, 41, 53, 69),
                accent: Color.FromArgb(255, 47, 123, 255),
                selection: Color.FromArgb(255, 31, 43, 56),
                hover: Color.FromArgb(255, 25, 36, 47),
                text: Color.FromArgb(255, 243, 246, 250),
                mutedText: Color.FromArgb(255, 152, 165, 181),
                subtleText: Color.FromArgb(255, 115, 129, 149)),
        };

        ApplyPalette(palette);
        ApplyTitleBarTheme(isLightTheme);
    }

    public IServiceProvider Services => host.Services;

    #endregion

    #region Protected Methods

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = host.Services.GetRequiredService<MainWindow>();
        ApplyTheme(ThemeKind.System);
        window.Activate();
    }

    #endregion

    #region Private Methods

    private void SetBrush(string key, Color color)
    {
        Resources[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
    }

    private void ApplyPalette(ThemePalette palette)
    {
        SetBrush("ShellBackgroundBrush", palette.Background);
        SetBrush("ShellChromeBrush", palette.Chrome);
        SetBrush("ShellPanelBrush", palette.Panel);
        SetBrush("ShellPanelAltBrush", palette.PanelAlt);
        SetBrush("ShellRaisedBrush", palette.Raised);
        SetBrush("ShellCodeSurfaceBrush", palette.CodeSurface);
        SetBrush("ShellInputBrush", palette.Input);
        SetBrush("ShellSelectionBrush", palette.Selection);
        SetBrush("ShellHoverBrush", palette.Hover);
        SetBrush("ShellOverlayBrush", palette.Overlay);
        SetBrush("ShellHairlineBrush", palette.Hairline);
        SetBrush("ShellBorderBrush", palette.Hairline);
        SetBrush("ShellAccentBrush", palette.Accent);
        SetBrush("ShellAccentSoftBrush", palette.AccentSoft);
        SetBrush("ShellTextBrush", palette.Text);
        SetBrush("ShellMutedTextBrush", palette.MutedText);
        SetBrush("ShellSubtleTextBrush", palette.SubtleText);
    }

    private void ApplyTitleBarTheme(bool isLightTheme)
    {
        if (window is null || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        window.AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        window.AppWindow.TitleBar.ButtonForegroundColor = isLightTheme ? Color.FromArgb(255, 37, 45, 57) : Microsoft.UI.Colors.White;
        window.AppWindow.TitleBar.ButtonInactiveForegroundColor = isLightTheme
            ? Color.FromArgb(255, 96, 108, 124)
            : Color.FromArgb(255, 154, 164, 178);
        window.AppWindow.TitleBar.ButtonHoverBackgroundColor = isLightTheme
            ? Color.FromArgb(255, 223, 230, 239)
            : Color.FromArgb(255, 33, 40, 50);
        window.AppWindow.TitleBar.ButtonHoverForegroundColor = isLightTheme ? Color.FromArgb(255, 20, 24, 31) : Microsoft.UI.Colors.White;
        window.AppWindow.TitleBar.ButtonPressedBackgroundColor = isLightTheme
            ? Color.FromArgb(255, 210, 219, 231)
            : Color.FromArgb(255, 45, 54, 66);
        window.AppWindow.TitleBar.ButtonPressedForegroundColor = isLightTheme ? Color.FromArgb(255, 20, 24, 31) : Microsoft.UI.Colors.White;
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        if (activeTheme != ThemeKind.System || window is null)
        {
            return;
        }

        window.DispatcherQueue.TryEnqueue(() => ApplyTheme(activeTheme));
    }

    private bool IsSystemDarkTheme()
    {
        var background = uiSettings.GetColorValue(UIColorType.Background);
        return background.R + background.G + background.B < 382;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs eventArgs)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ForRest-startup.log");
            File.AppendAllText(
                logPath,
                $"{DateTimeOffset.UtcNow:O} {eventArgs.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    #endregion

    private static ThemePalette CreateLightPalette(Color accent)
    {
        return new ThemePalette(
            Background: Color.FromArgb(255, 247, 249, 252),
            Chrome: Color.FromArgb(255, 239, 243, 248),
            Panel: Color.FromArgb(255, 252, 253, 255),
            PanelAlt: Color.FromArgb(255, 244, 247, 251),
            Raised: Color.FromArgb(255, 238, 242, 247),
            CodeSurface: Color.FromArgb(255, 250, 252, 255),
            Input: Color.FromArgb(255, 255, 255, 255),
            Hairline: Color.FromArgb(255, 208, 218, 231),
            Accent: accent,
            AccentSoft: Color.FromArgb(255, 223, 234, 255),
            Selection: Color.FromArgb(255, 229, 236, 245),
            Hover: Color.FromArgb(255, 236, 242, 248),
            Overlay: Color.FromArgb(120, 34, 41, 52),
            Text: Color.FromArgb(255, 22, 28, 36),
            MutedText: Color.FromArgb(255, 91, 103, 118),
            SubtleText: Color.FromArgb(255, 115, 126, 139));
    }

    private static ThemePalette CreateDarkPalette(
        Color background,
        Color chrome,
        Color panel,
        Color panelAlt,
        Color raised,
        Color codeSurface,
        Color input,
        Color hairline,
        Color accent,
        Color selection,
        Color hover,
        Color text,
        Color mutedText,
        Color subtleText)
    {
        return new ThemePalette(
            Background: background,
            Chrome: chrome,
            Panel: panel,
            PanelAlt: panelAlt,
            Raised: raised,
            CodeSurface: codeSurface,
            Input: input,
            Hairline: hairline,
            Accent: accent,
            AccentSoft: Color.FromArgb(255, 34, 54, 82),
            Selection: selection,
            Hover: hover,
            Overlay: Color.FromArgb(150, 5, 7, 12),
            Text: text,
            MutedText: mutedText,
            SubtleText: subtleText);
    }

    private sealed record ThemePalette(
        Color Background,
        Color Chrome,
        Color Panel,
        Color PanelAlt,
        Color Raised,
        Color CodeSurface,
        Color Input,
        Color Hairline,
        Color Accent,
        Color AccentSoft,
        Color Selection,
        Color Hover,
        Color Overlay,
        Color Text,
        Color MutedText,
        Color SubtleText);
}
