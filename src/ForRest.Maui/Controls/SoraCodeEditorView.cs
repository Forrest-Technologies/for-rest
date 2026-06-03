namespace ForRest.Maui.Controls;

/// <summary>
/// Thin cross-platform <see cref="View"/> whose platform handler hosts the native Sora
/// <c>CodeEditor</c> on Android. <see cref="SoraEditorSurface"/> owns the bindable-property /
/// MVVM surface and drives this view; the view only carries the primitive state the handler
/// maps onto the native widget plus the events the widget raises back.
/// </summary>
public sealed class SoraCodeEditorView : View
{
    #region Command Names

    public const string MoveCursorCommand = "MoveCursor";
    public const string PasteTextCommand = "PasteText";
    public const string FocusEditorCommand = "FocusEditor";

    #endregion

    #region Editor State

    private string text = string.Empty;
    private string languageId = "forrest";
    private string themeKey = "forrest-azure";
    private double fontSize = 14d;
    private bool isReadOnly;
    private string editableRangesJson = "[]";
    private string diagnosticsJson = "[]";
    private string languageHelpJson = "[]";
    private bool enableResponseActions;

    #endregion

    #region Properties

    /// <summary>Authoritative document text. The handler keeps this in sync via <see cref="TextChanged"/>.</summary>
    public string Text
    {
        get => text;
        set
        {
            string next = value ?? string.Empty;
            if (string.Equals(text, next, StringComparison.Ordinal))
            {
                return;
            }

            text = next;
            Handler?.UpdateValue(nameof(Text));
        }
    }

    public string LanguageId
    {
        get => languageId;
        set => SetMapped(ref languageId, string.IsNullOrWhiteSpace(value) ? "plaintext" : value, nameof(LanguageId));
    }

    public string ThemeKey
    {
        get => themeKey;
        set => SetMapped(ref themeKey, string.IsNullOrWhiteSpace(value) ? "forrest-azure" : value, nameof(ThemeKey));
    }

    public double FontSize
    {
        get => fontSize;
        set => SetMapped(ref fontSize, double.IsFinite(value) && value > 0 ? value : 14d, nameof(FontSize));
    }

    public bool IsReadOnly
    {
        get => isReadOnly;
        set => SetMapped(ref isReadOnly, value, nameof(IsReadOnly));
    }

    public string EditableRangesJson
    {
        get => editableRangesJson;
        set => SetMapped(ref editableRangesJson, string.IsNullOrWhiteSpace(value) ? "[]" : value, nameof(EditableRangesJson));
    }

    public string DiagnosticsJson
    {
        get => diagnosticsJson;
        set => SetMapped(ref diagnosticsJson, string.IsNullOrWhiteSpace(value) ? "[]" : value, nameof(DiagnosticsJson));
    }

    public string LanguageHelpJson
    {
        get => languageHelpJson;
        set => SetMapped(ref languageHelpJson, string.IsNullOrWhiteSpace(value) ? "[]" : value, nameof(LanguageHelpJson));
    }

    public bool EnableResponseActions
    {
        get => enableResponseActions;
        set => SetMapped(ref enableResponseActions, value, nameof(EnableResponseActions));
    }

    #endregion

    #region Events

    /// <summary>Raised by the handler whenever the native editor content changes.</summary>
    public event EventHandler<string>? TextChanged;

    public event EventHandler? SendRequested;

    public event EventHandler? UndoRequested;

    public event EventHandler? RedoRequested;

    public event EventHandler<EditorCursorPositionChangedEventArgs>? CursorChanged;

    public event EventHandler<EditorFocusChangedEventArgs>? FocusChanged;

    public event EventHandler<EditorResponseVarRequestEventArgs>? ResponseVarCopyRequested;

    #endregion

    #region Handler Callbacks

    /// <summary>Invoked by the handler when the native content changes. Updates <see cref="Text"/> without re-pushing it to the widget.</summary>
    public void NotifyTextChanged(string value)
    {
        string next = value ?? string.Empty;
        if (string.Equals(text, next, StringComparison.Ordinal))
        {
            return;
        }

        text = next;
        TextChanged?.Invoke(this, text);
    }

    public void NotifySendRequested() => SendRequested?.Invoke(this, EventArgs.Empty);

    public void NotifyUndoRequested() => UndoRequested?.Invoke(this, EventArgs.Empty);

    public void NotifyRedoRequested() => RedoRequested?.Invoke(this, EventArgs.Empty);

    public void NotifyCursorChanged(int lineNumber, int column) =>
        CursorChanged?.Invoke(this, new EditorCursorPositionChangedEventArgs(lineNumber, column));

    public void NotifyFocusChanged(bool isFocused) =>
        FocusChanged?.Invoke(this, new EditorFocusChangedEventArgs(isFocused));

    public void NotifyResponseVarCopyRequested(int lineNumber, int column) =>
        ResponseVarCopyRequested?.Invoke(this, new EditorResponseVarRequestEventArgs(lineNumber, column));

    #endregion

    #region Commands

    public void MoveCursor(int lineNumber, int column) =>
        Handler?.Invoke(MoveCursorCommand, new EditorCursorPositionChangedEventArgs(lineNumber, column));

    public void PasteText(string clipboardText) =>
        Handler?.Invoke(PasteTextCommand, clipboardText ?? string.Empty);

    public void FocusEditor() => Handler?.Invoke(FocusEditorCommand, null);

    #endregion

    #region Helpers

    private void SetMapped<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Handler?.UpdateValue(propertyName);
    }

    #endregion
}
