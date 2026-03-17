using ForRest.App.ViewModels;
using ForRest.Infrastructure.Sqlite;
using ForRest.Plugins.Host;
using ForRest.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace ForRest.App;

public partial class App : Application
{
    #region Private Fields

    private readonly IHost host;

    private Window? window;

    #endregion

    #region Constructors

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;

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
        var isLightTheme = theme is ThemeKind.Light or ThemeKind.Azure;
        if (window?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = isLightTheme ? ElementTheme.Light : ElementTheme.Dark;
        }

        var palette = theme switch
        {
            ThemeKind.Light => new ThemePalette(Microsoft.UI.Colors.White, Color.FromArgb(255, 244, 247, 250), Color.FromArgb(255, 220, 226, 234), Color.FromArgb(255, 70, 132, 255)),
            ThemeKind.Azure => new ThemePalette(Color.FromArgb(255, 244, 249, 255), Color.FromArgb(255, 230, 241, 255), Color.FromArgb(255, 197, 220, 255), Color.FromArgb(255, 0, 120, 212)),
            ThemeKind.Black => new ThemePalette(Color.FromArgb(255, 10, 10, 12), Color.FromArgb(255, 20, 20, 24), Color.FromArgb(255, 44, 44, 50), Color.FromArgb(255, 104, 197, 255)),
            ThemeKind.AmberDark => new ThemePalette(Color.FromArgb(255, 26, 20, 14), Color.FromArgb(255, 36, 28, 20), Color.FromArgb(255, 77, 58, 34), Color.FromArgb(255, 245, 158, 11)),
            ThemeKind.Custom => new ThemePalette(Color.FromArgb(255, 19, 22, 28), Color.FromArgb(255, 28, 32, 41), Color.FromArgb(255, 58, 65, 79), Color.FromArgb(255, 112, 124, 255)),
            _ => new ThemePalette(Color.FromArgb(255, 19, 22, 28), Color.FromArgb(255, 25, 29, 37), Color.FromArgb(255, 50, 58, 75), Color.FromArgb(255, 63, 142, 252)),
        };

        SetBrush("ShellBackgroundBrush", palette.Background);
        SetBrush("ShellPanelBrush", palette.Panel);
        SetBrush("ShellRaisedBrush", palette.Panel);
        SetBrush("ShellBorderBrush", palette.Border);
        SetBrush("ShellChromeBrush", isLightTheme ? Color.FromArgb(255, 236, 242, 250) : Color.FromArgb(255, 15, 20, 27));
        SetBrush("ShellCodeSurfaceBrush", isLightTheme ? Color.FromArgb(255, 248, 251, 255) : Color.FromArgb(255, 13, 18, 24));
        SetBrush("ShellHairlineBrush", isLightTheme ? Color.FromArgb(255, 206, 217, 232) : Color.FromArgb(255, 39, 49, 65));
        SetBrush("ShellAccentBrush", palette.Accent);
        SetBrush("ShellMutedTextBrush", isLightTheme ? Color.FromArgb(255, 91, 103, 118) : Color.FromArgb(255, 154, 164, 178));
    }

    public IServiceProvider Services => host.Services;

    #endregion

    #region Protected Methods

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = host.Services.GetRequiredService<MainWindow>();
        ApplyTheme(ThemeKind.Dark);
        window.Activate();
    }

    #endregion

    #region Private Methods

    private void SetBrush(string key, Color color)
    {
        Resources[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
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

    private sealed record ThemePalette(Color Background, Color Panel, Color Border, Color Accent);
}
