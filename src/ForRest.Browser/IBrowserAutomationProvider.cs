namespace ForRest.Browser;

/// <summary>
/// Supplies the currently connected browser automation bridge, if any. The MAUI app registers a
/// provider backed by the live embedded browser pane; headless contexts use
/// <see cref="NullBrowserAutomationProvider"/>. This is the seam that lets the execution pipeline stay
/// unaware of the UI while still routing <c>browser.*</c> calls to a real page when one is open.
/// </summary>
public interface IBrowserAutomationProvider
{
    IBrowserAutomationBridge Current { get; }
}

/// <summary>A provider that always returns the no-op bridge; used when no browser pane is connected.</summary>
public sealed class NullBrowserAutomationProvider : IBrowserAutomationProvider
{
    public static NullBrowserAutomationProvider Instance { get; } = new();

    public IBrowserAutomationBridge Current => NullBrowserBridge.Instance;
}
