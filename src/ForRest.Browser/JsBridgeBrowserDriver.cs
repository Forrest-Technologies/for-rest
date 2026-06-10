namespace ForRest.Browser;

/// <summary>
/// Drives an embedded WebView purely through JavaScript evaluation, implementing the high-level
/// <see cref="IBrowserAutomationBridge"/> for platforms that do not expose the Chrome DevTools Protocol
/// (notably Android's <c>android.webkit.WebView</c>). It reuses the same injected "eyes" scripts as the
/// CDP driver to locate elements, snapshot the page, and read text/attributes, and performs input by
/// dispatching synthetic DOM events. A visible red cursor is moved to the target before interactions so
/// a watching human can follow along.
///
/// Synthetic events are not OS-trusted input, so a small number of sites that gate on
/// <c>event.isTrusted</c> will not react — that is the documented trade-off of a JS bridge versus CDP.
/// All page IO goes through <see cref="IBrowserPageTransport"/>, so the driver is fully unit-testable
/// against a fake transport.
/// </summary>
public sealed class JsBridgeBrowserDriver(IBrowserPageTransport transport) : IBrowserAutomationBridge
{
    #region Private Fields

    private bool cursorInstalled;
    private CursorPoint cursor;

    #endregion

    #region Properties

    public bool IsAvailable => transport.IsAvailable;

    #endregion

    #region Public Methods

    public async Task Navigate(string url, CancellationToken cancellationToken = default)
    {
        await transport.Navigate(url, cancellationToken);

        // A navigation wipes injected DOM, so the cursor overlay has to be re-installed afterwards.
        cursorInstalled = false;
        await EnsureCursor(cancellationToken);
    }

    public async Task<BrowserElementInfo> Query(BrowserTarget target, CancellationToken cancellationToken = default)
    {
        JsonNode? node = await EvaluateJson(BrowserJs.Locate(target), cancellationToken);
        return ElementLocator.FromJson(node);
    }

    public async Task<bool> Exists(BrowserTarget target, CancellationToken cancellationToken = default)
    {
        BrowserElementInfo info = await Query(target, cancellationToken);
        return info.Found;
    }

    public async Task Click(BrowserTarget target, CursorMotion? motion = null, CancellationToken cancellationToken = default)
    {
        await MoveCursorTo(target, motion ?? CursorMotion.Default, cancellationToken);
        await Operate(target, "click", string.Empty, cancellationToken);
    }

    public async Task Type(BrowserTarget target, string text, CursorMotion? motion = null, CancellationToken cancellationToken = default)
    {
        await MoveCursorTo(target, motion ?? CursorMotion.Default, cancellationToken);
        await Operate(target, "type", text, cancellationToken);
    }

    public async Task Press(string keys, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(keys))
        {
            return;
        }

        await transport.Evaluate(BrowserJs.PressKey(keys), cancellationToken);
    }

    public async Task Hover(BrowserTarget target, CursorMotion? motion = null, CancellationToken cancellationToken = default)
    {
        await MoveCursorTo(target, motion ?? CursorMotion.Default, cancellationToken);
        await Operate(target, "hover", string.Empty, cancellationToken);
    }

    public Task<string> GetText(BrowserTarget target, CancellationToken cancellationToken = default) =>
        Operate(target, "text", string.Empty, cancellationToken);

    public Task<string> GetAttribute(BrowserTarget target, string name, CancellationToken cancellationToken = default) =>
        Operate(target, "attr", name, cancellationToken);

    public Task ScrollTo(BrowserTarget target, CancellationToken cancellationToken = default) =>
        Operate(target, "scroll", string.Empty, cancellationToken);

    public Task Select(BrowserTarget target, string value, CancellationToken cancellationToken = default) =>
        Operate(target, "select", value, cancellationToken);

    public async Task<BrowserElementInfo> WaitFor(BrowserTarget target, int timeoutMs, CancellationToken cancellationToken = default)
    {
        int budget = Math.Max(0, timeoutMs);
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(budget);
        while (true)
        {
            BrowserElementInfo info = await Query(target, cancellationToken);
            if (info.Found)
            {
                return info;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"Element {target} did not appear within {budget}ms.");
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    public async Task<BrowserSnapshot> Snapshot(CancellationToken cancellationToken = default)
    {
        JsonNode? node = await EvaluateJson(BrowserJs.Snapshot(), cancellationToken);
        return ElementLocator.SnapshotFromJson(node);
    }

    public Task<string> Screenshot(CancellationToken cancellationToken = default) =>
        transport.CaptureScreenshot(cancellationToken);

    public async Task<string> Evaluate(string expression, CancellationToken cancellationToken = default)
    {
        string result = await transport.Evaluate(expression, cancellationToken);
        return result ?? string.Empty;
    }

    #endregion

    #region Private Methods

    private async Task<string> Operate(BrowserTarget target, string operation, string argument, CancellationToken cancellationToken)
    {
        JsonNode? node = await EvaluateJson(BrowserJs.Operate(target, operation, argument), cancellationToken);
        if (node is null || node["ok"]?.GetValue<bool>() != true)
        {
            throw new InvalidOperationException($"Element {target} was not found.");
        }

        JsonNode? value = node["value"];
        return value is null ? string.Empty : value.ToString();
    }

    private async Task MoveCursorTo(BrowserTarget target, CursorMotion motion, CancellationToken cancellationToken)
    {
        if (!motion.Visible)
        {
            return;
        }

        BrowserElementInfo info = await Query(target, cancellationToken);
        if (!info.Found)
        {
            return;
        }

        await EnsureCursor(cancellationToken);

        // Animate the overlay along a human-like curved path (same easing/jitter as the CDP driver)
        // instead of teleporting, so the ghost cursor reads as a real hand on WebView platforms too.
        CursorPoint destination = new(info.X, info.Y);
        IReadOnlyList<CursorPoint> path = ElementLocator.HumanPath(cursor, destination, motion);
        for (int i = 0; i < path.Count; i++)
        {
            CursorPoint point = path[i];
            await transport.Evaluate(BrowserJs.MoveCursor(point.X, point.Y), cancellationToken);

            int delay = ElementLocator.StepDelay(motion.StepDelayMs, (double)(i + 1) / path.Count);
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        cursor = destination;
    }

    private async Task EnsureCursor(CancellationToken cancellationToken)
    {
        if (cursorInstalled)
        {
            return;
        }

        await transport.Evaluate(BrowserJs.InstallCursor(), cancellationToken);
        cursorInstalled = true;
    }

    private async Task<JsonNode?> EvaluateJson(string expression, CancellationToken cancellationToken)
    {
        string raw = await transport.Evaluate(expression, cancellationToken);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        // The host evaluate API hands back the result as a JSON-encoded string, so the JSON the page
        // returned is wrapped in one extra layer of quoting; unwrap it when present.
        JsonNode? node = Parse(raw);
        if (node is JsonValue value && value.TryGetValue(out string? inner) && !string.IsNullOrEmpty(inner))
        {
            return Parse(inner);
        }

        return node;
    }

    private static JsonNode? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json, "null", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    #endregion
}
