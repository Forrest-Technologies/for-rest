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

	[TestMethod]
	public void WorkbenchCenterPane_toolbar_exposes_paste_for_monaco_and_fallback_editors()
	{
		string xamlPath = Path.GetFullPath(
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
				"WorkbenchCenterPane.xaml"));
		string xaml = File.ReadAllText(xamlPath);
		StringAssert.Contains(xaml, "Text=\"Paste\"");
		StringAssert.Contains(xaml, "Clicked=\"OnPasteClicked\"");

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
		StringAssert.Contains(source, "private async void OnPasteClicked(object? sender, EventArgs e)");
		StringAssert.Contains(source, "public async Task PasteActiveDocumentAsync()");
		StringAssert.Contains(source, "await monacoEditor.PasteFromClipboardAsync();");
		StringAssert.Contains(source, "await editorSurface.PasteFromClipboardAsync();");
	}
}
