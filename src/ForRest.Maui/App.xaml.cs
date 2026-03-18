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

		return new Window(mainPage)
		{
			Title = "For-Rest"
		};
	}
}
