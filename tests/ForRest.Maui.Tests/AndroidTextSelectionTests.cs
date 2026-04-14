namespace ForRest.Maui.Tests;

[TestClass]
public sealed class AndroidTextSelectionTests
{
    #region Public Methods

    [TestMethod]
    public void MauiProgram_registers_selectable_editor_handler_for_android()
    {
        string source = GetNormalizedSource("src", "ForRest.Maui", "MauiProgram.cs");
        StringAssert.Contains(source, "SelectableEditorHandler");
    }

    [TestMethod]
    public void SelectableEditorHandler_enables_text_selection()
    {
        string source = GetNormalizedSource(
            "src", "ForRest.Maui", "Platforms", "Android", "Handlers", "SelectableEditorHandler.cs");
        StringAssert.Contains(source, "SetTextIsSelectable(true)");
        StringAssert.Contains(source, "LongClickable = true");
        StringAssert.Contains(source, "SelectionActionModeCallback");
    }

    [TestMethod]
    public void SelectableEditorHandler_preserves_selection_for_readonly_editors()
    {
        string source = GetNormalizedSource(
            "src", "ForRest.Maui", "Platforms", "Android", "Handlers", "SelectableEditorHandler.cs");
        StringAssert.Contains(source, "MapIsReadOnlySelectable");
        StringAssert.Contains(source, "KeyListener = null");
        StringAssert.Contains(source, "FocusableInTouchMode = true");
    }

    [TestMethod]
    public void AndroidMainPage_has_select_all_button()
    {
        string source = GetNormalizedSource("src", "ForRest.Maui", "AndroidMainPage.xaml");
        StringAssert.Contains(source, "OnSelectAllOutputClicked");
        StringAssert.Contains(source, "Select All");
    }

    #endregion

    #region Private Methods

    private static string GetNormalizedSource(params string[] pathSegments)
    {
        string sourcePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                Path.Combine(pathSegments)));
        return File.ReadAllText(sourcePath).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    #endregion
}
