using ForRest.Browser;

namespace ForRest.Maui.Services.Browser;

/// <summary>
/// App-wide holder for the currently connected browser automation bridge. The live browser pane calls
/// <see cref="Connect"/> once its embedded WebView is ready and <see cref="Disconnect"/> when it tears
/// down. Until a pane connects, <see cref="Current"/> is the no-op bridge, so scripts and MCP tools get
/// a clear "no browser pane" error instead of a crash. This is the single seam the execution pipeline
/// and MCP host resolve through, keeping them unaware of the WebView itself.
/// </summary>
public sealed class BrowserAutomationProvider : IBrowserAutomationProvider
{
    #region Private Fields

    private volatile IBrowserAutomationBridge current = NullBrowserBridge.Instance;

    #endregion

    #region Properties

    public IBrowserAutomationBridge Current => current;

    #endregion

    #region Public Methods

    public void Connect(IBrowserAutomationBridge bridge) => current = bridge;

    public void Disconnect() => current = NullBrowserBridge.Instance;

    #endregion
}
