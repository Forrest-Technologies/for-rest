using ForRest.Maui.Services;
using Microsoft.Maui.Devices;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class PlatformExperienceTests
{
	[TestMethod]
	public void UseWebCodeEditors_returns_true_for_windows()
	{
		Assert.IsTrue(PlatformExperience.UseWebCodeEditors(DevicePlatform.WinUI));
	}

	[TestMethod]
	public void UseWebCodeEditors_returns_false_for_android()
	{
		// Android now hosts the native Sora editor, not the Monaco WebView.
		Assert.IsFalse(PlatformExperience.UseWebCodeEditors(DevicePlatform.Android));
	}

	[TestMethod]
	public void UseSoraEditor_returns_true_for_android()
	{
		Assert.IsTrue(PlatformExperience.UseSoraEditor(DevicePlatform.Android));
	}

	[TestMethod]
	public void UseSoraEditor_returns_false_for_windows()
	{
		Assert.IsFalse(PlatformExperience.UseSoraEditor(DevicePlatform.WinUI));
	}

	[TestMethod]
	public void SupportsThemeConfigWatcher_returns_true_for_windows()
	{
		Assert.IsTrue(PlatformExperience.SupportsThemeConfigWatcher(DevicePlatform.WinUI));
	}

	[TestMethod]
	public void SupportsThemeConfigWatcher_returns_false_for_android()
	{
		Assert.IsFalse(PlatformExperience.SupportsThemeConfigWatcher(DevicePlatform.Android));
	}
}
