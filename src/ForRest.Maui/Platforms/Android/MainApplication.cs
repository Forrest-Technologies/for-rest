using Android.App;
using Android.Runtime;
using ForRest.Maui.Services;

namespace ForRest.Maui;

[Application]
public class MainApplication : MauiApplication
{
	private static int _exceptionHandlersRegistered;

	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
		RegisterExceptionHandlers();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	private static void RegisterExceptionHandlers()
	{
		if (Interlocked.Exchange(ref _exceptionHandlersRegistered, 1) != 0)
		{
			return;
		}

		AndroidEnvironment.UnhandledExceptionRaiser += OnAndroidUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
	}

	private static void OnAndroidUnhandledException(object? sender, RaiseThrowableEventArgs e)
	{
		AppLaunchGuard.RecordException("Android unhandled exception.", e.Exception);
	}

	private static void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
	{
		AppLaunchGuard.RecordException("Android AppDomain unhandled exception.", e.ExceptionObject as Exception);
	}

	private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		AppLaunchGuard.RecordException("Android task scheduler unobserved task exception.", e.Exception);
	}
}
