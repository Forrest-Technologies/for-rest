#if WINDOWS
using Microsoft.UI.Windowing;
using WinRT.Interop;
#endif

namespace ForRest.Maui.Theming;

public static class WindowChromeStyler
{
	public static void Apply(Window window, ShellThemeDefinition theme)
	{
#if WINDOWS
		if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow ||
		    !AppWindowTitleBar.IsCustomizationSupported())
		{
			return;
		}

		IntPtr hWnd = WindowNative.GetWindowHandle(nativeWindow);
		var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
		AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
		AppWindowTitleBar titleBar = appWindow.TitleBar;

		Windows.UI.Color activeBackground = ToPlatformColor(theme.Colors.WindowChromeBackgroundColor);
		Windows.UI.Color activeForeground = ToPlatformColor(theme.Colors.WindowChromeForegroundColor);
		Windows.UI.Color inactiveBackground = ToPlatformColor(theme.Colors.WindowChromeInactiveBackgroundColor);
		Windows.UI.Color inactiveForeground = ToPlatformColor(theme.Colors.WindowChromeInactiveForegroundColor);
		Windows.UI.Color hoverBackground = ToPlatformColor(theme.Colors.SelectionSoftColor);
		Windows.UI.Color pressedBackground = ToPlatformColor(theme.Colors.SelectionStrongColor);

		titleBar.BackgroundColor = activeBackground;
		titleBar.ForegroundColor = activeForeground;
		titleBar.InactiveBackgroundColor = inactiveBackground;
		titleBar.InactiveForegroundColor = inactiveForeground;
		titleBar.ButtonBackgroundColor = activeBackground;
		titleBar.ButtonForegroundColor = activeForeground;
		titleBar.ButtonInactiveBackgroundColor = inactiveBackground;
		titleBar.ButtonInactiveForegroundColor = inactiveForeground;
		titleBar.ButtonHoverBackgroundColor = hoverBackground;
		titleBar.ButtonHoverForegroundColor = activeForeground;
		titleBar.ButtonPressedBackgroundColor = pressedBackground;
		titleBar.ButtonPressedForegroundColor = activeForeground;
#endif
	}

#if WINDOWS
	private static Windows.UI.Color ToPlatformColor(string hexColor)
	{
		Microsoft.Maui.Graphics.Color color = ThemeSupport.ToColor(hexColor);
		return Windows.UI.Color.FromArgb(
			(byte)Math.Round(color.Alpha * 255),
			(byte)Math.Round(color.Red * 255),
			(byte)Math.Round(color.Green * 255),
			(byte)Math.Round(color.Blue * 255));
	}
#endif
}
