using System.Diagnostics;
using Android.Content;
using Android.Graphics;
using Android.Views.InputMethods;
using ForRest.Maui.Controls;
using IO.Github.Rosemoe.Sora.Event;
using IO.Github.Rosemoe.Sora.Lang.Empty;
using IO.Github.Rosemoe.Sora.Widget;
using IO.Github.Rosemoe.Sora.Widget.Schemes;

namespace ForRest.Maui.Platforms.Android.Editor;

/// <summary>
/// Owns the imperative interaction with the native Sora <c>CodeEditor</c>: applies cross-platform
/// state (text, language, theme, font, read-only, editable ranges, diagnostics, completion) and
/// relays the editor's content / selection / focus / key events back to <see cref="SoraCodeEditorView"/>.
/// </summary>
internal sealed class ForRestSoraEditorController : IDisposable
{
    #region Private Fields

    private readonly CodeEditor editor;
    private SoraCodeEditorView? view;
    private EditorEventBridge? eventBridge;
    private string currentLanguageId = "forrest";
    private string currentThemeKey = "forrest-azure";
    private string editableRangesJson = "[]";
    private string diagnosticsJson = "[]";
    private string languageHelpJson = "[]";
    private bool responseActionsEnabled;
    private bool suppressContentEvents;

    #endregion

    #region Constructors

    public ForRestSoraEditorController(CodeEditor editor)
    {
        this.editor = editor;
    }

    #endregion

    #region Configuration

    public void Configure()
    {
        try
        {
            editor.SetTypefaceText(Typeface.Monospace);
            editor.SetTextSize(14f);
            editor.EditorLanguage = new EmptyLanguage();
            editor.ColorScheme = ForRestSoraColorScheme.Build(ForRestEditorPalette.Resolve(currentThemeKey));
            // Keep behaviour close to the desktop Monaco surface.
            editor.Wordwrap = false;
            editor.Editable = true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraEditorController] Failed to configure editor defaults.{Environment.NewLine}{exception}");
        }
    }

    public void Attach(SoraCodeEditorView? virtualView)
    {
        view = virtualView;
        if (view is null)
        {
            return;
        }

        try
        {
            eventBridge = new EditorEventBridge(this);
            eventBridge.Subscribe(editor);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraEditorController] Failed to subscribe to editor events.{Environment.NewLine}{exception}");
        }

        // Apply any state the view already carries (object-initializer values, early bindings).
        SetLanguage(view.LanguageId);
        SetTheme(view.ThemeKey);
        SetFontSize(view.FontSize);
        SetReadOnly(view.IsReadOnly);
        SetText(view.Text);
    }

    public void Detach()
    {
        try
        {
            eventBridge?.Unsubscribe();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraEditorController] Failed to unsubscribe editor events.{Environment.NewLine}{exception}");
        }

        eventBridge = null;
        view = null;
    }

    #endregion

    #region State Application

    public void SetText(string? text)
    {
        string next = text ?? string.Empty;
        if (string.Equals(editor.Text?.ToString(), next, StringComparison.Ordinal))
        {
            return;
        }

        suppressContentEvents = true;
        try
        {
            editor.SetText(next);
        }
        finally
        {
            suppressContentEvents = false;
        }
    }

    public void SetLanguage(string? languageId)
    {
        currentLanguageId = string.IsNullOrWhiteSpace(languageId) ? "plaintext" : languageId;
        if (!ForRestSoraTextMate.TryApplyLanguage(editor, currentLanguageId))
        {
            // TextMate grammar unavailable: keep the editor usable as plain text.
            try
            {
                editor.EditorLanguage = new EmptyLanguage();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[ForRestSoraEditorController] Failed to set empty language.{Environment.NewLine}{exception}");
            }
        }
    }

    public void SetTheme(string? themeKey)
    {
        currentThemeKey = string.IsNullOrWhiteSpace(themeKey) ? "forrest-azure" : themeKey;
        ForRestEditorPalette palette = ForRestEditorPalette.Resolve(currentThemeKey);
        try
        {
            // Prefer the TextMate colour scheme (covers token colours); fall back to the
            // programmatic chrome scheme so the editor is always themed.
            EditorColorScheme scheme = ForRestSoraTextMate.TryBuildColorScheme(currentThemeKey)
                ?? ForRestSoraColorScheme.Build(palette);
            editor.ColorScheme = scheme;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraEditorController] Failed to apply theme '{currentThemeKey}'.{Environment.NewLine}{exception}");
        }
    }

    public void SetFontSize(double fontSize)
    {
        try
        {
            editor.SetTextSize((float)(double.IsFinite(fontSize) && fontSize > 0 ? fontSize : 14d));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraEditorController] Failed to apply font size.{Environment.NewLine}{exception}");
        }
    }

    public void SetReadOnly(bool isReadOnly)
    {
        try
        {
            editor.Editable = !isReadOnly;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraEditorController] Failed to apply read-only state.{Environment.NewLine}{exception}");
        }
    }

    public void SetEditableRanges(string? json)
    {
        editableRangesJson = string.IsNullOrWhiteSpace(json) ? "[]" : json;
        // Constrained editing parity is enforced in the content bridge; the parsed ranges are
        // consulted there. Stored here so the bridge always sees the latest constraint set.
    }

    public string EditableRangesJson => editableRangesJson;

    public void SetDiagnostics(string? json)
    {
        diagnosticsJson = string.IsNullOrWhiteSpace(json) ? "[]" : json;
        ForRestSoraDiagnostics.Apply(editor, diagnosticsJson);
    }

    public void SetLanguageHelp(string? json)
    {
        languageHelpJson = string.IsNullOrWhiteSpace(json) ? "[]" : json;
        ForRestSoraTextMate.TryApplyCompletions(editor, currentLanguageId, languageHelpJson);
    }

    public void SetResponseActionsEnabled(bool enabled)
    {
        responseActionsEnabled = enabled;
    }

    public bool ResponseActionsEnabled => responseActionsEnabled;

    #endregion

    #region Commands

    public void MoveCursor(int lineNumber, int column)
    {
        try
        {
            int targetLine = Math.Max(0, lineNumber - 1);
            int lineCount = editor.LineCount;
            if (targetLine >= lineCount)
            {
                targetLine = Math.Max(0, lineCount - 1);
            }

            int maxColumn = editor.Text?.GetColumnCount(targetLine) ?? 0;
            int targetColumn = Math.Clamp(column - 1, 0, Math.Max(0, maxColumn));
            editor.SetSelection(targetLine, targetColumn);
            FocusEditor();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraEditorController] Failed to move cursor.{Environment.NewLine}{exception}");
        }
    }

    public void PasteText(string? text)
    {
        if (string.IsNullOrEmpty(text) || !editor.Editable)
        {
            return;
        }

        try
        {
            var cursor = editor.Cursor;
            editor.Text?.Insert(cursor.LeftLine, cursor.LeftColumn, text);
            FocusEditor();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraEditorController] Failed to paste text.{Environment.NewLine}{exception}");
        }
    }

    public void FocusEditor()
    {
        try
        {
            editor.RequestFocus();
            if (editor.Context?.GetSystemService(Context.InputMethodService) is InputMethodManager inputManager)
            {
                inputManager.ShowSoftInput(editor, ShowFlags.Implicit);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraEditorController] Failed to focus editor.{Environment.NewLine}{exception}");
        }
    }

    #endregion

    #region Event Relay

    internal void RaiseTextChanged()
    {
        if (suppressContentEvents)
        {
            return;
        }

        view?.NotifyTextChanged(editor.Text?.ToString() ?? string.Empty);
    }

    internal void RaiseCursorChanged()
    {
        try
        {
            var cursor = editor.Cursor;
            view?.NotifyCursorChanged(cursor.LeftLine + 1, cursor.LeftColumn + 1);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ForRestSoraEditorController] Failed to report cursor.{Environment.NewLine}{exception}");
        }
    }

    internal void RaiseFocusChanged(bool isFocused) => view?.NotifyFocusChanged(isFocused);

    internal void RaiseSendRequested() => view?.NotifySendRequested();

    internal void RaiseUndoRequested() => view?.NotifyUndoRequested();

    internal void RaiseRedoRequested() => view?.NotifyRedoRequested();

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Detach();
    }

    #endregion
}
