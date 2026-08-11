using Microsoft.Maui.Devices;

namespace ForRest.Maui.Services;

public static class PlatformExperience
{
	public static DevicePlatform CurrentPlatform => DeviceInfo.Current.Platform;

	public static bool UseWebCodeEditors()
	{
		return UseWebCodeEditors(CurrentPlatform);
	}

	public static bool UseWebCodeEditors(DevicePlatform platform)
	{
		// Android now hosts the native Sora code editor instead of the Monaco WebView, so the
		// WebView-backed editors are limited to the desktop shells (WinUI and Mac Catalyst).
		return platform == DevicePlatform.WinUI || platform == DevicePlatform.MacCatalyst;
	}

	public static bool UseSoraEditor()
	{
		return UseSoraEditor(CurrentPlatform);
	}

	public static bool UseSoraEditor(DevicePlatform platform)
	{
		// Mobile (Android) uses the native Sora editor, which provides real syntax highlighting,
		// IME handling, and selection behaviour without the WebView bridge the desktop build needs.
		return platform == DevicePlatform.Android;
	}

	public static bool SupportsThemeConfigWatcher()
	{
		return SupportsThemeConfigWatcher(CurrentPlatform);
	}

	public static bool SupportsThemeConfigWatcher(DevicePlatform platform)
	{
		return platform == DevicePlatform.WinUI;
	}
}
