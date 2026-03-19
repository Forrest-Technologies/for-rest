using Microsoft.Extensions.DependencyInjection;
using ForRest.Maui.Theming;

namespace ForRest.Maui;

public partial class App : Application
{
	private readonly IServiceProvider _services;
	private readonly IThemeService _themeService;

	public App(IServiceProvider services, IThemeService themeService)
	{
		InitializeComponent();
		_services = services;
		_themeService = themeService;
		_themeService.ThemeChanged += OnThemeChanged;
		_themeService.Start();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		MainPage mainPage = _services.GetRequiredService<MainPage>();

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
}
