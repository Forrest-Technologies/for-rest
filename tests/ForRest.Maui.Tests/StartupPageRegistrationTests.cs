using System.IO;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class StartupPageRegistrationTests
{
	[TestMethod]
	public void MauiProgram_registers_startup_pages_as_transient()
	{
		string source = File.ReadAllText(GetRepoFile("src", "ForRest.Maui", "MauiProgram.cs"));

		StringAssert.Contains(source, "AddTransient<MainPage>()");
		StringAssert.Contains(source, "AddTransient<AndroidMainPage>()");
		Assert.IsFalse(source.Contains("AddSingleton<MainPage>()", StringComparison.Ordinal));
	}

	[TestMethod]
	public void AndroidMainPage_uses_constructor_injection_for_main_view_model()
	{
		string source = File.ReadAllText(GetRepoFile("src", "ForRest.Maui", "AndroidMainPage.xaml.cs"));

		StringAssert.Contains(source, "public AndroidMainPage(MainPageViewModel viewModel)");
		StringAssert.Contains(source, "BindingContext = viewModel;");
		Assert.IsFalse(source.Contains("EnsureBindingContext()", StringComparison.Ordinal));
	}

	private static string GetRepoFile(params string[] parts)
	{
		string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
		return Path.Combine([root, .. parts]);
	}
}
