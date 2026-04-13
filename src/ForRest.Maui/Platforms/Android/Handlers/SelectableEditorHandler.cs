using Android.Views;
using AndroidX.AppCompat.Widget;
using Microsoft.Maui.Handlers;

namespace ForRest.Maui.Platforms.Android.Handlers;

public class SelectableEditorHandler : EditorHandler
{
    #region Constructors

    public SelectableEditorHandler() : base()
    {
        Mapper.AppendToMapping(nameof(IEditor.IsReadOnly), MapIsReadOnlySelectable);
    }

    #endregion

    #region Public Methods

    protected override void ConnectHandler(AppCompatEditText platformView)
    {
        base.ConnectHandler(platformView);
        EnableTextSelection(platformView);
    }

    #endregion

    #region Private Methods

    private static void EnableTextSelection(AppCompatEditText editText)
    {
        editText.SetTextIsSelectable(true);
        editText.LongClickable = true;
        editText.CustomSelectionActionModeCallback = new SelectionActionModeCallback();
    }

    private static void MapIsReadOnlySelectable(IEditorHandler handler, IEditor editor)
    {
        if (handler.PlatformView is not AppCompatEditText editText)
        {
            return;
        }

        editText.Focusable = true;
        editText.FocusableInTouchMode = true;
        editText.SetTextIsSelectable(true);

        if (editor.IsReadOnly)
        {
            editText.KeyListener = null;
        }
    }

    #endregion
}

internal sealed class SelectionActionModeCallback : Java.Lang.Object, ActionMode.ICallback
{
    #region Interface Implementations

    public bool OnCreateActionMode(ActionMode? mode, IMenu? menu) => true;

    public bool OnPrepareActionMode(ActionMode? mode, IMenu? menu) => false;

    public bool OnActionItemClicked(ActionMode? mode, IMenuItem? item) => false;

    public void OnDestroyActionMode(ActionMode? mode) { }

    #endregion
}
