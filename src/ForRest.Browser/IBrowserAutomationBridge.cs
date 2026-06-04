namespace ForRest.Browser;

/// <summary>
/// The high-level, engine-agnostic browser automation surface. This is the single contract the
/// scripting <c>browser</c> API and the MCP browser tools depend on, keeping them free of any CDP or
/// WebView detail. The Windows/macOS implementation drives an embedded WebView2 via CDP; Android
/// supplies a JavaScript-bridge implementation of the same surface.
/// </summary>
public interface IBrowserAutomationBridge
{
    /// <summary>Whether a live browser pane is connected and ready to be driven.</summary>
    bool IsAvailable { get; }

    Task Navigate(string url, CancellationToken cancellationToken = default);

    Task<BrowserElementInfo> Query(BrowserTarget target, CancellationToken cancellationToken = default);

    Task<bool> Exists(BrowserTarget target, CancellationToken cancellationToken = default);

    Task Click(BrowserTarget target, CursorMotion? motion = null, CancellationToken cancellationToken = default);

    Task Type(BrowserTarget target, string text, CursorMotion? motion = null, CancellationToken cancellationToken = default);

    Task Press(string keys, CancellationToken cancellationToken = default);

    Task Hover(BrowserTarget target, CursorMotion? motion = null, CancellationToken cancellationToken = default);

    Task<string> GetText(BrowserTarget target, CancellationToken cancellationToken = default);

    Task<string> GetAttribute(BrowserTarget target, string name, CancellationToken cancellationToken = default);

    Task<BrowserElementInfo> WaitFor(BrowserTarget target, int timeoutMs, CancellationToken cancellationToken = default);

    Task ScrollTo(BrowserTarget target, CancellationToken cancellationToken = default);

    Task Select(BrowserTarget target, string value, CancellationToken cancellationToken = default);

    Task<BrowserSnapshot> Snapshot(CancellationToken cancellationToken = default);

    /// <summary>Returns a base64-encoded PNG screenshot of the current page.</summary>
    Task<string> Screenshot(CancellationToken cancellationToken = default);

    /// <summary>Evaluates a JavaScript expression in the page and returns the result as JSON text.</summary>
    Task<string> Evaluate(string expression, CancellationToken cancellationToken = default);
}
