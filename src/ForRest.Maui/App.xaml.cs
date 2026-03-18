using Microsoft.Extensions.DependencyInjection;

namespace ForRest.Maui;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
		_services = services;
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

		return window;
	}
}
