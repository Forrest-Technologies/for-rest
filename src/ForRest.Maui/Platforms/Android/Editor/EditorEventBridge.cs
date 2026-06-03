using System.Diagnostics;
using Android.Views;
using IO.Github.Rosemoe.Sora.Event;
using IO.Github.Rosemoe.Sora.Widget;
using Java.Lang;

namespace ForRest.Maui.Platforms.Android.Editor;

/// <summary>
/// Bridges Sora's event stream to <see cref="ForRestSoraEditorController"/>. Subscribes to content,
/// selection, focus and key events and maps them onto the relay methods that ultimately surface as
/// cross-platform editor events (text changed, cursor moved, focus changed, send / undo / redo).
/// </summary>
internal sealed class EditorEventBridge : Java.Lang.Object, IEventReceiver
{
    #region Private Fields

    private readonly ForRestSoraEditorController controller;
    private readonly List<SubscriptionReceipt> receipts = [];

    #endregion

    #region Constructors

    public EditorEventBridge(ForRestSoraEditorController controller)
    {
        this.controller = controller;
    }

    #endregion

    #region Subscription

    public void Subscribe(CodeEditor editor)
    {
        SubscribeEvent(editor, typeof(ContentChangeEvent));
        SubscribeEvent(editor, typeof(SelectionChangeEvent));
        SubscribeEvent(editor, typeof(EditorFocusChangeEvent));
        SubscribeEvent(editor, typeof(EditorKeyEvent));
    }

    public void Unsubscribe()
    {
        foreach (SubscriptionReceipt receipt in receipts)
        {
            try
            {
                receipt.Unsubscribe();
            }
            catch (System.Exception exception)
            {
                Debug.WriteLine($"[EditorEventBridge] Failed to unsubscribe event.{System.Environment.NewLine}{exception}");
            }
        }

        receipts.Clear();
    }

    private void SubscribeEvent(CodeEditor editor, System.Type eventType)
    {
        try
        {
            Class? eventClass = Class.FromType(eventType);
            if (eventClass is null)
            {
                return;
            }

            SubscriptionReceipt? receipt = editor.SubscribeEvent(eventClass, this);
            if (receipt is not null)
            {
                receipts.Add(receipt);
            }
        }
        catch (System.Exception exception)
        {
            Debug.WriteLine($"[EditorEventBridge] Failed to subscribe to {eventType.Name}.{System.Environment.NewLine}{exception}");
        }
    }

    #endregion

    #region IEventReceiver

    public void OnReceive(Event p0, Unsubscribe p1)
    {
        switch (p0)
        {
            case ContentChangeEvent:
                controller.RaiseTextChanged();
                controller.RaiseCursorChanged();
                break;
            case SelectionChangeEvent:
                controller.RaiseCursorChanged();
                break;
            case EditorFocusChangeEvent focusEvent:
                controller.RaiseFocusChanged(focusEvent.IsGainFocus);
                break;
            case EditorKeyEvent keyEvent:
                HandleKeyEvent(keyEvent);
                break;
        }
    }

    #endregion

    #region Key Handling

    private void HandleKeyEvent(EditorKeyEvent keyEvent)
    {
        // Only act on the initial key-down; mirror the Monaco keybindings: Ctrl/Cmd+Enter and F5
        // request a send, Ctrl+Z / Ctrl+Y relay undo / redo to the host view-model (the source of
        // truth for document history) instead of letting the editor manage it locally.
        KeyEvent? androidKeyEvent = keyEvent.KeyEvent;
        if (androidKeyEvent is null || androidKeyEvent.Action != KeyEventActions.Down)
        {
            return;
        }

        bool ctrl = androidKeyEvent.IsCtrlPressed;
        Keycode code = androidKeyEvent.KeyCode;

        if ((ctrl && code == Keycode.Enter) || code == Keycode.F5)
        {
            controller.RaiseSendRequested();
            keyEvent.Intercept();
            return;
        }

        if (ctrl && code == Keycode.Z && !androidKeyEvent.IsShiftPressed)
        {
            controller.RaiseUndoRequested();
            keyEvent.Intercept();
            return;
        }

        if (ctrl && (code == Keycode.Y || (code == Keycode.Z && androidKeyEvent.IsShiftPressed)))
        {
            controller.RaiseRedoRequested();
            keyEvent.Intercept();
        }
    }

    #endregion
}
