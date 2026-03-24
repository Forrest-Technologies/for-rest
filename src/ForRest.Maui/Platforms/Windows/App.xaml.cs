using ForRest.Maui.Services;
using Microsoft.UI.Xaml;

namespace ForRest.Maui.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object. This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		AppLaunchGuard.Initialize();
		UnhandledException += OnUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		AppLaunchGuard.RecordException("WinUI unhandled exception.", e.Exception);
	}

	private void OnCurrentDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
	{
		AppLaunchGuard.RecordException("AppDomain unhandled exception.", e.ExceptionObject as Exception);
	}

	private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		AppLaunchGuard.RecordException("TaskScheduler unobserved task exception.", e.Exception);
	}
}
