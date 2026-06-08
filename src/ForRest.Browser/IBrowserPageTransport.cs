namespace ForRest.Browser;

/// <summary>
/// The thin page-control seam the <see cref="JsBridgeBrowserDriver"/> drives. It is the JavaScript-bridge
/// analogue of <see cref="ICdpTransport"/>: a platform supplies a small adapter over its embedded
/// WebView (evaluate a script, navigate and wait for load, capture a screenshot) and the driver builds
/// every higher-level action on top. Keeping this interface tiny is what lets the driver be unit-tested
/// against a fake transport with no WebView in sight — the same way the CDP driver is tested.
/// </summary>
public interface IBrowserPageTransport
{
    /// <summary>Whether a live WebView is connected and ready to be driven.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Evaluates a JavaScript expression in the page and returns its result. The result is the raw value
    /// the host's evaluate API hands back — typically a JSON-encoded string that the driver unwraps.
    /// </summary>
    Task<string> Evaluate(string script, CancellationToken cancellationToken = default);

    /// <summary>Navigates the page to a URL and resolves once the page has finished loading (or a bounded timeout).</summary>
    Task Navigate(string url, CancellationToken cancellationToken = default);

    /// <summary>Captures a screenshot of the current page and returns it as base64-encoded PNG data.</summary>
    Task<string> CaptureScreenshot(CancellationToken cancellationToken = default);
}
