namespace ForRest.Browser;

/// <summary>
/// Optional capability a page transport can implement to deliver <em>OS-trusted</em> input: real touch
/// events the platform dispatches through its normal input pipeline, so the DOM events the page receives
/// carry <c>event.isTrusted = true</c>. When a transport offers this, <see cref="JsBridgeBrowserDriver"/>
/// prefers it for taps, so the few sites that gate on trusted input (opening windows, starting media,
/// paste) react to the synthetic "ghost" the same way they would to a real finger. Transports that cannot
/// provide trusted input simply do not implement this interface and the driver uses synthetic DOM events.
/// </summary>
public interface IBrowserTrustedInput
{
    /// <summary>Whether trusted taps can currently be delivered.</summary>
    bool SupportsTrustedTap { get; }

    /// <summary>
    /// Delivers a trusted tap at a page coordinate (CSS pixels, relative to the viewport — exactly the
    /// coordinates an element rect reports). Returns <c>true</c> when the tap was dispatched and
    /// <c>false</c> when it could not be, so the caller can fall back to a synthetic click.
    /// </summary>
    Task<bool> TryTrustedTap(double cssX, double cssY, CancellationToken cancellationToken = default);
}
