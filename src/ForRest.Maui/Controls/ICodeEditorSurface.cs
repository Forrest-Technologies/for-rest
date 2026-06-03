namespace ForRest.Maui.Controls;

/// <summary>
/// Platform-neutral contract for the workbench code-editor surface. Implemented by the
/// WebView-hosted <see cref="MonacoEditorSurface"/> (Windows / desktop) and the native
/// <see cref="SoraEditorSurface"/> (Android). Hosting panes bind against the concrete
/// control's bindable properties but drive editor behaviour through this interface so the
/// rest of the app never depends on a specific editor implementation.
/// </summary>
public interface ICodeEditorSurface
{
    #region Events

    /// <summary>Raised when the user invokes the "send" shortcut (Ctrl/Cmd+Enter, F5).</summary>
    event EventHandler? SendRequested;

    /// <summary>Raised when the user invokes the editor undo shortcut.</summary>
    event EventHandler? UndoRequested;

    /// <summary>Raised when the user invokes the editor redo shortcut.</summary>
    event EventHandler? RedoRequested;

    /// <summary>Raised when the user requests a response-variable copy from a read-only viewer.</summary>
    event EventHandler<EditorResponseVarRequestEventArgs>? ResponseVarCopyRequested;

    /// <summary>Raised when the caret moves, reporting a 1-based line / column.</summary>
    event EventHandler<EditorCursorPositionChangedEventArgs>? CursorPositionChanged;

    /// <summary>Raised when the editor gains or loses text focus.</summary>
    event EventHandler<EditorFocusChangedEventArgs>? EditorFocusChanged;

    #endregion

    #region Methods

    /// <summary>Pushes the latest editor text back into the bound <c>Text</c> property.</summary>
    Task FlushTextSyncAsync();

    /// <summary>Pastes the current clipboard text at the caret. Returns true when text was inserted.</summary>
    Task<bool> PasteFromClipboardAsync();

    /// <summary>Moves the caret to the supplied 1-based line / column and reveals it.</summary>
    Task MoveCursorToAsync(int lineNumber, int column);

    #endregion
}

public sealed class EditorResponseVarRequestEventArgs(int lineNumber, int column) : EventArgs
{
    public int LineNumber { get; } = lineNumber;

    public int Column { get; } = column;
}

public sealed class EditorCursorPositionChangedEventArgs(int lineNumber, int column) : EventArgs
{
    public int LineNumber { get; } = lineNumber;

    public int Column { get; } = column;
}

public sealed class EditorFocusChangedEventArgs(bool isFocused) : EventArgs
{
    public bool IsFocused { get; } = isFocused;
}
