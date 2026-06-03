using Android.Graphics;
using IO.Github.Rosemoe.Sora.Widget.Schemes;

namespace ForRest.Maui.Platforms.Android.Editor;

/// <summary>
/// Editor chrome palette for a single ForRest theme. Token (syntax) colours are carried by the
/// bundled TextMate theme JSON; this palette covers the surfaces Sora paints directly
/// (background, gutter, caret, selection, current line) so the editor still matches the active
/// theme even when TextMate highlighting is unavailable.
/// </summary>
internal sealed record ForRestEditorPalette(
    string Background,
    string Foreground,
    string GutterBackground,
    string LineNumber,
    string LineNumberActive,
    string CurrentLine,
    string Selection,
    string Cursor,
    string Whitespace,
    string IndentGuide,
    string IndentGuideActive)
{
    #region Palette Table

    // Mirrors the editor.* colours defined for each Monaco theme in MonacoEditorSurface so the
    // native and desktop editors render the same chrome for a given ThemeKey.
    private static readonly Dictionary<string, ForRestEditorPalette> Palettes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["forrest-light"] = new(
            "#FCFDFE", "#16202A", "#F2F4F7", "#9CA7B3", "#384552", "#F5F7FA",
            "#E1EAF2", "#16202A", "#D5DDE6", "#E6EBF0", "#C9D2DB"),
        ["forrest-azure"] = new(
            "#EDF5FD", "#0F2236", "#E2EDF8", "#5E7B97", "#1E3B57", "#E1F0FC",
            "#CFE4F8", "#0F2236", "#B3C9DE", "#C5D8EB", "#9FBAD5"),
        ["forrest-dark"] = new(
            "#141B24", "#EEF3F7", "#1A232D", "#6F8092", "#C5D2DE", "#19222D",
            "#29425F", "#EEF3F7", "#2E3A46", "#27313A", "#3E4B57"),
        ["forrest-black"] = new(
            "#101419", "#F3F6F8", "#151B21", "#66727F", "#E4EBF1", "#141A20",
            "#243341", "#F3F6F8", "#27313B", "#202932", "#34414D"),
        ["forrest-amber"] = new(
            "#FFFCF6", "#2B2218", "#F5ECDD", "#A08F79", "#6A5034", "#FBF3E6",
            "#F2DFC0", "#6D4A20", "#E1D3BE", "#E9DCC8", "#D5BE97"),
    };

    #endregion

    #region Lookup

    public static ForRestEditorPalette Resolve(string? themeKey)
    {
        if (!string.IsNullOrWhiteSpace(themeKey) && Palettes.TryGetValue(themeKey, out ForRestEditorPalette? palette))
        {
            return palette;
        }

        return Palettes["forrest-azure"];
    }

    #endregion
}

internal static class ForRestSoraColorScheme
{
    #region Factory

    /// <summary>
    /// Builds an <see cref="EditorColorScheme"/> configured with the supplied palette's chrome
    /// colours. Used as the baseline scheme and as the fallback when TextMate theming is not loaded.
    /// </summary>
    public static EditorColorScheme Build(ForRestEditorPalette palette)
    {
        EditorColorScheme scheme = new();
        scheme.SetColor(EditorColorScheme.WholeBackground, ParseColor(palette.Background));
        scheme.SetColor(EditorColorScheme.TextNormal, ParseColor(palette.Foreground));
        scheme.SetColor(EditorColorScheme.LineNumberBackground, ParseColor(palette.GutterBackground));
        scheme.SetColor(EditorColorScheme.LineNumberPanel, ParseColor(palette.GutterBackground));
        scheme.SetColor(EditorColorScheme.LineNumber, ParseColor(palette.LineNumber));
        scheme.SetColor(EditorColorScheme.LineNumberCurrent, ParseColor(palette.LineNumberActive));
        scheme.SetColor(EditorColorScheme.CurrentLine, ParseColor(palette.CurrentLine));
        scheme.SetColor(EditorColorScheme.SelectedTextBackground, ParseColor(palette.Selection));
        scheme.SetColor(EditorColorScheme.SelectionInsert, ParseColor(palette.Cursor));
        scheme.SetColor(EditorColorScheme.SelectionHandle, ParseColor(palette.Cursor));
        scheme.SetColor(EditorColorScheme.NonPrintableChar, ParseColor(palette.Whitespace));
        scheme.SetColor(EditorColorScheme.BlockLine, ParseColor(palette.IndentGuide));
        scheme.SetColor(EditorColorScheme.BlockLineCurrent, ParseColor(palette.IndentGuideActive));
        return scheme;
    }

    #endregion

    #region Helpers

    private static int ParseColor(string hex)
    {
        try
        {
            return Color.ParseColor(hex);
        }
        catch
        {
            return Color.ParseColor("#0F2236");
        }
    }

    #endregion
}
