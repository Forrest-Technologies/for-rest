using System.Diagnostics;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace ForRest.Maui.Controls;

/// <summary>
/// Native code-editor surface for mobile (Android), backed by the Sora <c>CodeEditor</c> widget.
/// Mirrors the bindable-property / event surface of <see cref="MonacoEditorSurface"/> so the
/// workbench and inspector panes can host either implementation behind <see cref="ICodeEditorSurface"/>
/// without caring which one is active.
/// </summary>
public sealed class SoraEditorSurface : ContentView, ICodeEditorSurface
{
    #region Bindable Properties

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(SoraEditorSurface),
        string.Empty,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: OnTextChanged);

    public static readonly BindableProperty LanguageProperty = BindableProperty.Create(
        nameof(Language),
        typeof(string),
        typeof(SoraEditorSurface),
        "forrest",
        propertyChanged: OnLanguageChanged);

    public static readonly BindableProperty ThemeKeyProperty = BindableProperty.Create(
        nameof(ThemeKey),
        typeof(string),
        typeof(SoraEditorSurface),
        "forrest-azure",
        propertyChanged: OnThemeKeyChanged);

    public static readonly BindableProperty EditorFontSizeProperty = BindableProperty.Create(
        nameof(EditorFontSize),
        typeof(double),
        typeof(SoraEditorSurface),
        14d,
        propertyChanged: OnEditorFontSizeChanged);

    public static readonly BindableProperty EditableRangesJsonProperty = BindableProperty.Create(
        nameof(EditableRangesJson),
        typeof(string),
        typeof(SoraEditorSurface),
        "[]",
        propertyChanged: OnEditableRangesJsonChanged);

    public static readonly BindableProperty DiagnosticsJsonProperty = BindableProperty.Create(
        nameof(DiagnosticsJson),
        typeof(string),
        typeof(SoraEditorSurface),
        "[]",
        propertyChanged: OnDiagnosticsJsonChanged);

    public static readonly BindableProperty LanguageHelpJsonProperty = BindableProperty.Create(
        nameof(LanguageHelpJson),
        typeof(string),
        typeof(SoraEditorSurface),
        "[]",
        propertyChanged: OnLanguageHelpJsonChanged);

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(SoraEditorSurface),
        false,
        propertyChanged: OnIsReadOnlyChanged);

    public static readonly BindableProperty EnableResponseActionsProperty = BindableProperty.Create(
        nameof(EnableResponseActions),
        typeof(bool),
        typeof(SoraEditorSurface),
        false,
        propertyChanged: OnEnableResponseActionsChanged);

    public static readonly BindableProperty RequestedCursorLineNumberProperty = BindableProperty.Create(
        nameof(RequestedCursorLineNumber),
        typeof(int),
        typeof(SoraEditorSurface),
        0);

    public static readonly BindableProperty RequestedCursorColumnProperty = BindableProperty.Create(
        nameof(RequestedCursorColumn),
        typeof(int),
        typeof(SoraEditorSurface),
        0);

    public static readonly BindableProperty RequestedCursorVersionProperty = BindableProperty.Create(
        nameof(RequestedCursorVersion),
        typeof(int),
        typeof(SoraEditorSurface),
        0,
        propertyChanged: OnRequestedCursorVersionChanged);

    #endregion

    #region Private Fields

    private readonly SoraCodeEditorView editorView;
    private bool isSyncingTextFromEditor;

    #endregion

    #region Events

    public event EventHandler? SendRequested;

    public event EventHandler? UndoRequested;

    public event EventHandler? RedoRequested;

    public event EventHandler<EditorResponseVarRequestEventArgs>? ResponseVarCopyRequested;

    public event EventHandler<EditorCursorPositionChangedEventArgs>? CursorPositionChanged;

    public event EventHandler<EditorFocusChangedEventArgs>? EditorFocusChanged;

    #endregion

    #region Constructors

    public SoraEditorSurface()
    {
        editorView = new SoraCodeEditorView();
        editorView.TextChanged += OnEditorTextChanged;
        editorView.SendRequested += OnEditorSendRequested;
        editorView.UndoRequested += OnEditorUndoRequested;
        editorView.RedoRequested += OnEditorRedoRequested;
        editorView.CursorChanged += OnEditorCursorChanged;
        editorView.FocusChanged += OnEditorFocusChanged;
        editorView.ResponseVarCopyRequested += OnEditorResponseVarCopyRequested;

        // Push construction-time defaults so an editor created with object-initializer
        // properties (Language/IsReadOnly/EnableResponseActions) is configured before display.
        editorView.LanguageId = Language;
        editorView.ThemeKey = ThemeKey;
        editorView.FontSize = EditorFontSize;
        editorView.IsReadOnly = IsReadOnly;
        editorView.EnableResponseActions = EnableResponseActions;
        editorView.EditableRangesJson = EditableRangesJson;
        editorView.DiagnosticsJson = DiagnosticsJson;
        editorView.LanguageHelpJson = LanguageHelpJson;
        editorView.Text = Text;

        Content = editorView;
    }

    #endregion

    #region Properties

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Language
    {
        get => (string)GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    public string ThemeKey
    {
        get => (string)GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    public double EditorFontSize
    {
        get => (double)GetValue(EditorFontSizeProperty);
        set => SetValue(EditorFontSizeProperty, value);
    }

    public string EditableRangesJson
    {
        get => (string)GetValue(EditableRangesJsonProperty);
        set => SetValue(EditableRangesJsonProperty, value);
    }

    public string DiagnosticsJson
    {
        get => (string)GetValue(DiagnosticsJsonProperty);
        set => SetValue(DiagnosticsJsonProperty, value);
    }

    public string LanguageHelpJson
    {
        get => (string)GetValue(LanguageHelpJsonProperty);
        set => SetValue(LanguageHelpJsonProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool EnableResponseActions
    {
        get => (bool)GetValue(EnableResponseActionsProperty);
        set => SetValue(EnableResponseActionsProperty, value);
    }

    public int RequestedCursorLineNumber
    {
        get => (int)GetValue(RequestedCursorLineNumberProperty);
        set => SetValue(RequestedCursorLineNumberProperty, value);
    }

    public int RequestedCursorColumn
    {
        get => (int)GetValue(RequestedCursorColumnProperty);
        set => SetValue(RequestedCursorColumnProperty, value);
    }

    public int RequestedCursorVersion
    {
        get => (int)GetValue(RequestedCursorVersionProperty);
        set => SetValue(RequestedCursorVersionProperty, value);
    }

    #endregion

    #region ICodeEditorSurface

    public Task FlushTextSyncAsync()
    {
        SyncTextFromEditor();
        return Task.CompletedTask;
    }

    public async Task<bool> PasteFromClipboardAsync()
    {
        if (IsReadOnly)
        {
            return false;
        }

        string? clipboardText = null;
        try
        {
            clipboardText = await Clipboard.Default.GetTextAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[SoraEditorSurface] Failed to read clipboard text for paste.{Environment.NewLine}{exception}");
        }

        if (string.IsNullOrEmpty(clipboardText))
        {
            return false;
        }

        editorView.PasteText(clipboardText);
        return true;
    }

    public Task MoveCursorToAsync(int lineNumber, int column)
    {
        editorView.MoveCursor(Math.Max(1, lineNumber), Math.Max(1, column));
        editorView.FocusEditor();
        return Task.CompletedTask;
    }

    #endregion

    #region Property Change Handlers

    private static void OnTextChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        SoraEditorSurface surface = (SoraEditorSurface)bindable;
        if (surface.isSyncingTextFromEditor)
        {
            return;
        }

        surface.editorView.Text = newValue as string ?? string.Empty;
    }

    private static void OnLanguageChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((SoraEditorSurface)bindable).editorView.LanguageId = newValue as string ?? "forrest";

    private static void OnThemeKeyChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((SoraEditorSurface)bindable).editorView.ThemeKey = newValue as string ?? "forrest-azure";

    private static void OnEditorFontSizeChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((SoraEditorSurface)bindable).editorView.FontSize = newValue is double size ? size : 14d;

    private static void OnEditableRangesJsonChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((SoraEditorSurface)bindable).editorView.EditableRangesJson = newValue as string ?? "[]";

    private static void OnDiagnosticsJsonChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((SoraEditorSurface)bindable).editorView.DiagnosticsJson = newValue as string ?? "[]";

    private static void OnLanguageHelpJsonChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((SoraEditorSurface)bindable).editorView.LanguageHelpJson = newValue as string ?? "[]";

    private static void OnIsReadOnlyChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((SoraEditorSurface)bindable).editorView.IsReadOnly = (bool)(newValue ?? false);

    private static void OnEnableResponseActionsChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((SoraEditorSurface)bindable).editorView.EnableResponseActions = (bool)(newValue ?? false);

    private static void OnRequestedCursorVersionChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        SoraEditorSurface surface = (SoraEditorSurface)bindable;
        int line = surface.RequestedCursorLineNumber;
        int column = surface.RequestedCursorColumn;
        if (line <= 0 || column <= 0)
        {
            return;
        }

        surface.editorView.MoveCursor(line, column);
    }

    #endregion

    #region Editor Event Handlers

    private void OnEditorTextChanged(object? sender, string value)
    {
        isSyncingTextFromEditor = true;
        try
        {
            Text = value;
        }
        finally
        {
            isSyncingTextFromEditor = false;
        }
    }

    private void OnEditorSendRequested(object? sender, EventArgs e)
    {
        SyncTextFromEditor();
        SendRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorUndoRequested(object? sender, EventArgs e)
    {
        SyncTextFromEditor();
        UndoRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorRedoRequested(object? sender, EventArgs e)
    {
        SyncTextFromEditor();
        RedoRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorCursorChanged(object? sender, EditorCursorPositionChangedEventArgs e) =>
        CursorPositionChanged?.Invoke(this, e);

    private void OnEditorFocusChanged(object? sender, EditorFocusChangedEventArgs e) =>
        EditorFocusChanged?.Invoke(this, e);

    private void OnEditorResponseVarCopyRequested(object? sender, EditorResponseVarRequestEventArgs e) =>
        ResponseVarCopyRequested?.Invoke(this, e);

    #endregion

    #region Helpers

    private void SyncTextFromEditor()
    {
        isSyncingTextFromEditor = true;
        try
        {
            Text = editorView.Text;
        }
        finally
        {
            isSyncingTextFromEditor = false;
        }
    }

    #endregion
}
