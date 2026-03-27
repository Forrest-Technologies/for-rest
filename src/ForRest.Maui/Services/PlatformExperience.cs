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
		return platform == DevicePlatform.WinUI || platform == DevicePlatform.Android;
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
