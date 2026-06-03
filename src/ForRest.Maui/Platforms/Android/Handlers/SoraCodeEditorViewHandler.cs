using Android.Content;
using Com.Forrest.Maui.Sora;
using ForRest.Maui.Controls;
using Microsoft.Maui.Handlers;

namespace ForRest.Maui.Platforms.Android.Handlers;

/// <summary>
/// Hosts the native Sora editor for <see cref="SoraCodeEditorView"/> on Android via the
/// <c>ForRestSoraEditor</c> Java facade (AndroidJavaSource). The facade wraps the Sora
/// <c>CodeEditor</c> and the tm4e TextMate stack behind a small primitive API, so this handler only
/// maps cross-platform state onto the facade and relays its listener callbacks back to the view.
/// </summary>
public sealed class SoraCodeEditorViewHandler : ViewHandler<SoraCodeEditorView, ForRestSoraEditor>
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
            [nameof(SoraCodeEditorView.DiagnosticsJson)] = MapDiagnostics,
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

    private SoraEditorListener? listener;

    #endregion

    #region Constructors

    public SoraCodeEditorViewHandler()
        : base(PropertyMapper, ViewCommandMapper)
    {
    }

    #endregion

    #region Lifecycle

    protected override ForRestSoraEditor CreatePlatformView()
    {
        Context context = Context
            ?? throw new InvalidOperationException("A platform Context is required to create the Sora editor.");
        return new ForRestSoraEditor(context);
    }

    protected override void ConnectHandler(ForRestSoraEditor platformView)
    {
        base.ConnectHandler(platformView);
        listener = new SoraEditorListener(VirtualView);
        platformView.SetListener(listener);

        // Apply the state the view already carries (object-initializer values, early bindings).
        platformView.SetLanguageId(VirtualView.LanguageId);
        platformView.SetThemeKey(VirtualView.ThemeKey);
        platformView.SetFontSize(VirtualView.FontSize);
        platformView.SetReadOnly(VirtualView.IsReadOnly);
        platformView.SetText(VirtualView.Text);
    }

    protected override void DisconnectHandler(ForRestSoraEditor platformView)
    {
        platformView.SetListener(null);
        listener?.Dispose();
        listener = null;
        base.DisconnectHandler(platformView);
    }

    #endregion

    #region Property Mappers

    private static void MapText(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.PlatformView?.SetText(view.Text);

    private static void MapLanguage(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.PlatformView?.SetLanguageId(view.LanguageId);

    private static void MapTheme(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.PlatformView?.SetThemeKey(view.ThemeKey);

    private static void MapFontSize(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.PlatformView?.SetFontSize(view.FontSize);

    private static void MapIsReadOnly(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.PlatformView?.SetReadOnly(view.IsReadOnly);

    private static void MapDiagnostics(SoraCodeEditorViewHandler handler, SoraCodeEditorView view) =>
        handler.PlatformView?.SetDiagnostics(view.DiagnosticsJson);

    #endregion

    #region Command Mappers

    private static void MapMoveCursor(SoraCodeEditorViewHandler handler, SoraCodeEditorView view, object? args)
    {
        if (args is EditorCursorPositionChangedEventArgs position)
        {
            handler.PlatformView?.MoveCursor(position.LineNumber, position.Column);
        }
    }

    private static void MapPasteText(SoraCodeEditorViewHandler handler, SoraCodeEditorView view, object? args)
    {
        if (args is string clipboardText)
        {
            handler.PlatformView?.PasteText(clipboardText);
        }
    }

    private static void MapFocusEditor(SoraCodeEditorViewHandler handler, SoraCodeEditorView view, object? args) =>
        handler.PlatformView?.FocusEditor();

    #endregion
}

/// <summary>
/// Bridges the Java facade's listener callbacks to the cross-platform <see cref="SoraCodeEditorView"/>.
/// </summary>
internal sealed class SoraEditorListener(SoraCodeEditorView view)
    : Java.Lang.Object, ForRestSoraEditor.IListener
{
    public void OnTextChanged(string? text) => view.NotifyTextChanged(text ?? string.Empty);

    public void OnCursorChanged(int line, int column) => view.NotifyCursorChanged(line, column);

    public void OnFocusChanged(bool focused) => view.NotifyFocusChanged(focused);

    public void OnSendRequested() => view.NotifySendRequested();

    public void OnUndoRequested() => view.NotifyUndoRequested();

    public void OnRedoRequested() => view.NotifyRedoRequested();
}
