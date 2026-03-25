using Microsoft.Extensions.DependencyInjection;
using ForRest.Maui.Theming;
using ForRest.Maui.Services;

namespace ForRest.Maui;

public partial class App : Application
{
	private readonly IServiceProvider _services;
	private readonly IThemeService _themeService;

	public App(IServiceProvider services, IThemeService themeService)
	{
		AppLaunchGuard.Initialize();
		InitializeComponent();
		_services = services;
		_themeService = themeService;
		_themeService.ThemeChanged += OnThemeChanged;
		try
		{
			_themeService.Start();
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Theme service startup failed.", exception);
		}
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		MainPage mainPage;
		try
		{
			mainPage = _services.GetRequiredService<MainPage>();
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Failed to resolve MainPage during window creation.", exception);
			ContentPage fallbackPage = BuildFallbackPage(exception);
			return new Window(fallbackPage)
			{
				Title = "For-Rest"
			};
		}

		Window window = new(mainPage)
		{
			Title = "For-Rest"
		};

		if (DeviceInfo.Platform == DevicePlatform.WinUI)
		{
			window.Width = 1480;
			window.Height = 920;
			window.MinimumWidth = 1120;
			window.MinimumHeight = 720;
		}

		window.HandlerChanged += (_, _) => WindowChromeStyler.Apply(window, _themeService.CurrentTheme);
		WindowChromeStyler.Apply(window, _themeService.CurrentTheme);

		return window;
	}

	private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
	{
		foreach (Window window in Windows)
		{
			WindowChromeStyler.Apply(window, e.Theme);
		}
	}

	private static ContentPage BuildFallbackPage(Exception exception)
	{
		return new ContentPage
		{
			Content = new ScrollView
			{
				Content = new VerticalStackLayout
				{
					Padding = new Thickness(24),
					Spacing = 12,
					Children =
					{
						new Label
						{
							Text = "ForRest started in recovery mode.",
							FontAttributes = FontAttributes.Bold,
							FontSize = 20
						},
						new Label
						{
							Text = "The main workbench could not be created. Startup details were written to the diagnostics log."
						},
						new Label
						{
							Text = AppLaunchGuard.StartupLogPath
						},
						new Label
						{
							Text = exception.ToString(),
							FontFamily = "OpenSansRegular"
						}
					}
				}
			}
		};
	}
}
