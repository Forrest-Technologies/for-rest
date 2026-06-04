namespace ForRest.Browser;

/// <summary>
/// Drives an embedded Chromium page over the Chrome DevTools Protocol, implementing the high-level
/// <see cref="IBrowserAutomationBridge"/>. It owns the "eyes" (locate/snapshot/screenshot) and "hands"
/// (cursor movement, clicks, typing) and animates the visible red cursor. It holds only the last
/// cursor position, so it is fully testable against a fake <see cref="ICdpTransport"/>.
/// </summary>
public sealed class CdpBrowserDriver(CdpClient client) : IBrowserAutomationBridge
{
    #region Private Fields

    private CursorPoint cursor;

    #endregion

    #region Properties

    public bool IsAvailable => true;

    #endregion

    #region Public Methods

    public Task Navigate(string url, CancellationToken cancellationToken = default) =>
        client.Navigate(url, cancellationToken);

    public async Task<BrowserElementInfo> Query(BrowserTarget target, CancellationToken cancellationToken = default)
    {
        JsonNode? node = await client.EvaluateJson(BrowserJs.Locate(target), cancellationToken);
        return ElementLocator.FromJson(node);
    }

    public async Task<bool> Exists(BrowserTarget target, CancellationToken cancellationToken = default)
    {
        BrowserElementInfo info = await Query(target, cancellationToken);
        return info.Found;
    }

    public async Task Click(BrowserTarget target, CursorMotion? motion = null, CancellationToken cancellationToken = default)
    {
        CursorMotion resolved = motion ?? CursorMotion.Default;
        BrowserElementInfo info = await RequireElement(target, cancellationToken);
        await MoveTo(info.X, info.Y, resolved, cancellationToken);
        if (resolved.Visible)
        {
            await client.Evaluate(BrowserJs.ClickRipple(info.X, info.Y), cancellationToken: cancellationToken);
        }

        await client.DispatchMouse("mousePressed", info.X, info.Y, "left", 1, cancellationToken);
        await client.DispatchMouse("mouseReleased", info.X, info.Y, "left", 1, cancellationToken);
    }

    public async Task Type(BrowserTarget target, string text, CursorMotion? motion = null, CancellationToken cancellationToken = default)
    {
        await Click(target, motion, cancellationToken);
        await client.InsertText(text, cancellationToken);
    }

    public async Task Press(string keys, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(keys))
        {
            return;
        }

        KeyStroke stroke = KeyMap.Resolve(keys);
        await client.DispatchKey(stroke.Key, stroke.Code, stroke.VirtualKeyCode, stroke.Text, cancellationToken);
    }

    public async Task Hover(BrowserTarget target, CursorMotion? motion = null, CancellationToken cancellationToken = default)
    {
        BrowserElementInfo info = await RequireElement(target, cancellationToken);
        await MoveTo(info.X, info.Y, motion ?? CursorMotion.Default, cancellationToken);
        await client.DispatchMouse("mouseMoved", info.X, info.Y, cancellationToken: cancellationToken);
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
        JsonNode? node = await client.EvaluateJson(BrowserJs.Snapshot(), cancellationToken);
        return ElementLocator.SnapshotFromJson(node);
    }

    public Task<string> Screenshot(CancellationToken cancellationToken = default) =>
        client.CaptureScreenshot(cancellationToken);

    public async Task<string> Evaluate(string expression, CancellationToken cancellationToken = default)
    {
        string? result = await client.Evaluate(expression, cancellationToken: cancellationToken);
        return result ?? string.Empty;
    }

    #endregion

    #region Private Methods

    private async Task<BrowserElementInfo> RequireElement(BrowserTarget target, CancellationToken cancellationToken)
    {
        BrowserElementInfo info = await Query(target, cancellationToken);
        if (!info.Found)
        {
            throw new InvalidOperationException($"Element {target} was not found.");
        }

        return info;
    }

    private async Task<string> Operate(BrowserTarget target, string operation, string argument, CancellationToken cancellationToken)
    {
        JsonNode? node = await client.EvaluateJson(BrowserJs.Operate(target, operation, argument), cancellationToken);
        if (node is null || node["ok"]?.GetValue<bool>() != true)
        {
            throw new InvalidOperationException($"Element {target} was not found.");
        }

        JsonNode? value = node["value"];
        return value is null ? string.Empty : value.ToString();
    }

    private async Task MoveTo(double x, double y, CursorMotion motion, CancellationToken cancellationToken)
    {
        IReadOnlyList<CursorPoint> path = ElementLocator.HumanPath(cursor, new CursorPoint(x, y), motion);
        for (int i = 0; i < path.Count; i++)
        {
            CursorPoint point = path[i];
            await client.DispatchMouse("mouseMoved", point.X, point.Y, cancellationToken: cancellationToken);
            if (motion.Visible)
            {
                await client.Evaluate(BrowserJs.MoveCursor(point.X, point.Y), cancellationToken: cancellationToken);
            }

            int delay = ElementLocator.StepDelay(motion.StepDelayMs, (double)(i + 1) / path.Count);
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        cursor = new CursorPoint(x, y);
    }

    #endregion
}
