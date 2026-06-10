namespace ForRest.Browser;

/// <summary>
/// A bridge used when no browser pane is connected. Every action fails with a clear, actionable
/// message rather than a null reference, so scripts and agents get useful feedback instead of crashes.
/// </summary>
public sealed class NullBrowserBridge : IBrowserAutomationBridge
{
    #region Private Fields

    private const string Unavailable =
        "No browser pane is connected. Open the Browser tab in For-Rest (desktop) before running browser automation.";

    #endregion

    #region Properties

    public static NullBrowserBridge Instance { get; } = new();

    public bool IsAvailable => false;

    #endregion

    #region Public Methods

    public Task Navigate(string url, CancellationToken cancellationToken = default) => throw Fail();

    public Task<BrowserElementInfo> Query(BrowserTarget target, CancellationToken cancellationToken = default) => throw Fail();

    public Task<bool> Exists(BrowserTarget target, CancellationToken cancellationToken = default) => throw Fail();

    public Task Click(BrowserTarget target, CursorMotion? motion = null, CancellationToken cancellationToken = default) => throw Fail();

    public Task Type(BrowserTarget target, string text, CursorMotion? motion = null, TypingCadence? cadence = null, CancellationToken cancellationToken = default) => throw Fail();

    public Task Press(string keys, CancellationToken cancellationToken = default) => throw Fail();

    public Task Hover(BrowserTarget target, CursorMotion? motion = null, CancellationToken cancellationToken = default) => throw Fail();

    public Task<string> GetText(BrowserTarget target, CancellationToken cancellationToken = default) => throw Fail();

    public Task<string> GetAttribute(BrowserTarget target, string name, CancellationToken cancellationToken = default) => throw Fail();

    public Task<BrowserElementInfo> WaitFor(BrowserTarget target, int timeoutMs, CancellationToken cancellationToken = default) => throw Fail();

    public Task ScrollTo(BrowserTarget target, CancellationToken cancellationToken = default) => throw Fail();

    public Task Select(BrowserTarget target, string value, CancellationToken cancellationToken = default) => throw Fail();

    public Task<BrowserSnapshot> Snapshot(CancellationToken cancellationToken = default) => throw Fail();

    public Task<string> Screenshot(CancellationToken cancellationToken = default) => throw Fail();

    public Task<string> Evaluate(string expression, CancellationToken cancellationToken = default) => throw Fail();

    #endregion

    #region Private Methods

    private static InvalidOperationException Fail() => new(Unavailable);

    #endregion
}
