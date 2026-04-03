using System.IO;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class WorkbenchCenterPaneSourceTests
{
	[TestMethod]
	public void WorkbenchCenterPane_fallback_editors_use_theme_resources_instead_of_hardcoded_dark_text()
	{
		string sourcePath = Path.GetFullPath(
			Path.Combine(
				AppContext.BaseDirectory,
				"..",
				"..",
				"..",
				"..",
				"..",
				"src",
				"ForRest.Maui",
				"Controls",
				"WorkbenchCenterPane.xaml.cs"));
		string source = File.ReadAllText(sourcePath);

		StringAssert.Contains(source, "editor.SetDynamicResource(InputView.TextColorProperty, ThemeResourceKeys.TextPrimaryColor);");
		StringAssert.Contains(source, "editor.SetDynamicResource(VisualElement.BackgroundColorProperty, ThemeResourceKeys.EditorBackgroundColor);");
		Assert.IsFalse(source.Contains("TextColor = Color.FromArgb(\"#16202A\")", StringComparison.Ordinal));
	}
}
