using Microsoft.Extensions.DependencyInjection;
using ForRest.Maui.Theming;
using ForRest.Maui.Services;
using ForRest.Maui.ViewModels;
#if WINDOWS || MACCATALYST
using ForRest.Maui.Services.Mcp;
#endif

namespace ForRest.Maui;

public partial class App : Application
{
	private readonly IServiceProvider _services;
	private readonly IThemeService _themeService;
	private int _shutdownState;

	public App(IServiceProvider services, IThemeService themeService)
	{
		AppLaunchGuard.Initialize();
		AppLaunchGuard.RecordMessage("App startup", "Application constructor entered.");
		InitializeComponent();
		_services = services;
		_themeService = themeService;
		_themeService.ThemeChanged += OnThemeChanged;
		try
		{
			RoslynRuntimeDirectoryBootstrapper.Initialize();
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Roslyn runtime directory initialization failed.", exception);
		}
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
		AppLaunchGuard.RecordMessage("App startup", "CreateWindow invoked.");
		Page shellPage;
		string shellPageName;
		try
		{
			(shellPage, shellPageName) = ResolveStartupPage();
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Failed to resolve the startup page during window creation.", exception);
			ContentPage fallbackPage = BuildFallbackPage(exception);
			return new Window(fallbackPage)
			{
				Title = "For-Rest"
			};
		}

		AppLaunchGuard.RecordMessage("Startup page resolved.", shellPageName);

		Window window = new(shellPage)
		{
			Title = "For-Rest"
		};

#if WINDOWS
		window.Width = 1480;
		window.Height = 920;
		window.MinimumWidth = 1120;
		window.MinimumHeight = 720;
#endif

		window.HandlerChanged += (_, _) => WindowChromeStyler.Apply(window, _themeService.CurrentTheme);
		window.Destroying += OnWindowDestroying;
		WindowChromeStyler.Apply(window, _themeService.CurrentTheme);

#if WINDOWS || MACCATALYST
		try
		{
			// Expose the live page view model to the MCP server so workspace/script
			// edits and active-document selection happen on the running UI (and
			// persist through the view model) instead of writing the state file
			// underneath the app, which it would overwrite.
			McpLiveWorkbenchAccessor? workbenchAccessor = _services.GetService<McpLiveWorkbenchAccessor>();
			if (workbenchAccessor is not null && _services.GetService<MainPageViewModel>() is IMcpWorkbenchBridge bridge)
			{
				workbenchAccessor.Current = bridge;
			}

			ForRestMcpServerLifecycle? mcpLifecycle = _services.GetService<ForRestMcpServerLifecycle>();
			if (mcpLifecycle is not null)
			{
				// Fire-and-forget; MCP server failures are logged internally and
				// must never block window creation or the UI thread.
				_ = mcpLifecycle.StartAsync();
			}
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("MCP server lifecycle startup failed.", exception);
		}
#endif

		return window;
	}

	private async void OnWindowDestroying(object? sender, EventArgs e)
	{
		if (Interlocked.Exchange(ref _shutdownState, 1) != 0)
		{
			return;
		}

		try
		{
			if (sender is Window window)
			{
				switch (window.Page)
				{
					case MainPage mainPage:
						await mainPage.PrepareForShutdownAsync();
						break;
					case AndroidMainPage androidMainPage:
						await androidMainPage.PrepareForShutdownAsync();
						break;
				}
			}
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Window shutdown preparation failed.", exception);
		}
		finally
		{
			_themeService.ThemeChanged -= OnThemeChanged;

#if WINDOWS || MACCATALYST
			try
			{
				ForRestMcpServerLifecycle? mcpLifecycle = _services.GetService<ForRestMcpServerLifecycle>();
				if (mcpLifecycle is not null)
				{
					await mcpLifecycle.StopAsync();
				}
			}
			catch (Exception exception)
			{
				AppLaunchGuard.RecordException("MCP server lifecycle shutdown failed.", exception);
			}
#endif

			try
			{
				if (_services is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}
			catch (Exception exception)
			{
				AppLaunchGuard.RecordException("Application service disposal failed during shutdown.", exception);
			}

#if WINDOWS
			try
			{
				Microsoft.UI.Xaml.Application.Current?.Exit();
			}
			catch (Exception exception)
			{
				AppLaunchGuard.RecordException("WinUI application exit failed during shutdown.", exception);
			}
#endif
		}
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

	private (Page Page, string Name) ResolveStartupPage()
	{
#if ANDROID
		if (AppLaunchGuard.IsSafeModeEnabled)
		{
			return (_services.GetRequiredService<AndroidMainPage>(), nameof(AndroidMainPage));
		}

		try
		{
			return (_services.GetRequiredService<MainPage>(), nameof(MainPage));
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException(
				"Failed to resolve the full Android workbench. Falling back to the Android recovery shell.",
				exception);
			return (_services.GetRequiredService<AndroidMainPage>(), nameof(AndroidMainPage));
		}
#else
		return (_services.GetRequiredService<MainPage>(), nameof(MainPage));
#endif
	}
}
