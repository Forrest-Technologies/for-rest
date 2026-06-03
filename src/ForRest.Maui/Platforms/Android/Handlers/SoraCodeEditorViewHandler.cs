using Android.Content;
using Android.Graphics;
using Microsoft.Maui.Handlers;
using ForRest.Maui.Controls;
using ForRest.Maui.Platforms.Android.Editor;
using IO.Github.Rosemoe.Sora.Widget;

namespace ForRest.Maui.Platforms.Android.Handlers;

/// <summary>
/// Hosts the native Sora <c>CodeEditor</c> for <see cref="SoraCodeEditorView"/> on Android.
/// Property/command mappers translate the cross-platform view state onto the widget and the
/// editor's event stream is relayed back to the view (and from there to the workbench panes).
/// </summary>
public sealed class SoraCodeEditorViewHandler : ViewHandler<SoraCodeEditorView, CodeEditor>
{
    #region Mappers

    public static readonly IPropertyMapper<SoraCodeEditorView, SoraCodeEditorViewHandler> PropertyMapper =
        new PropertyMapper<SoraCodeEditorView, SoraCodeEditorViewHandler>(ViewMapper)
        {
            [nameof(SoraCodeEditorView.Text)] = MapText,
            [nameof(SoraCodeEditorView.LanguageId)] = MapLanguage,
            [nameof(SoraCodeEditorView.ThemeKey)] = MapTheme,
            [nameof(SoraCodeEditorView.FontSize)] = MapFontSize,
            [nameof(SoraCodeEditorView.IsReadOnly)] = MapIsReadOnly,
            [nameof(SoraCodeEditorView.EditableRangesJson)] = MapEditableRanges,
            [nameof(SoraCodeEditorView.DiagnosticsJson)] = MapDiagnostics,
            [nameof(SoraCodeEditorView.LanguageHelpJson)] = MapLanguageHelp,
            [nameof(SoraCodeEditorView.EnableResponseActions)] = MapEnableResponseActions,
        };

    public static readonly CommandMapper<SoraCodeEditorView, SoraCodeEditorViewHandler> ViewCommandMapper =
        new(ViewCommandMapper)
        {
            [SoraCodeEditorView.MoveCursorCommand] = MapMoveCursor,
            [SoraCodeEditorView.PasteTextCommand] = MapPasteText,
            [SoraCodeEditorView.FocusEditorCommand] = MapFocusEditor,
        };

    #endregion

    #region Private Fields

    private ForRestSoraEditorController? controller;

    #endregion

    #region Constructors

    public SoraCodeEditorViewHandler()
        : base(PropertyMapper, ViewCommandMapper)
    {
    }

    #endregion

    #region Lifecycle

    protected override CodeEditor CreatePlatformView()
    {
        Context context = Context
            ?? throw new InvalidOperationException("A platform Context is required to create the Sora editor.");
        CodeEditor editor = new(context);
        controller = new ForRestSoraEditorController(editor);
        controller.Configure();
        return editor;
    }

    protected override void ConnectHandler(CodeEditor platformView)
    {
        base.ConnectHandler(platformView);
        controller?.Attach(VirtualView);
    }

    protected override void DisconnectHandler(CodeEditor platformView)
    {
        controller?.Detach();
        controller?.Dispose();
        controller = null;
        base.DisconnectHandler(platformView);
    }

    #endregion

    #region Property Mappers

    private static void MapText(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.controller?.SetText(view.Text);

    private static void MapLanguage(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.controller?.SetLanguage(view.LanguageId);

    private static void MapTheme(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.controller?.SetTheme(view.ThemeKey);

    private static void MapFontSize(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.controller?.SetFontSize(view.FontSize);

    private static void MapIsReadOnly(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.controller?.SetReadOnly(view.IsReadOnly);

    private static void MapEditableRanges(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.controller?.SetEditableRanges(view.EditableRangesJson);

    private static void MapDiagnostics(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.controller?.SetDiagnostics(view.DiagnosticsJson);

    private static void MapLanguageHelp(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.controller?.SetLanguageHelp(view.LanguageHelpJson);

    private static void MapEnableResponseActions(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.controller?.SetResponseActionsEnabled(view.EnableResponseActions);

    #endregion

    #region Command Mappers

    private static void MapMoveCursor(SoraCodeEditorViewHandler handler, SoraCodeEditorView view, object? args)
    {
        if (args is EditorCursorPositionChangedEventArgs position)
        {
            handler.controller?.MoveCursor(position.LineNumber, position.Column);
        }
    }

    private static void MapPasteText(SoraCodeEditorViewHandler handler, SoraCodeEditorView view, object? args)
    {
        if (args is string clipboardText)
        {
            handler.controller?.PasteText(clipboardText);
        }
    }

    private static void MapFocusEditor(SoraCodeEditorViewHandler handler, SoraCodeEditorView view, object? args) =>
        handler.controller?.FocusEditor();

    #endregion
}
