namespace ForRest.Browser;

/// <summary>
/// A raw Chrome DevTools Protocol transport. The MAUI platform layer supplies the implementation:
/// on Windows this wraps <c>CoreWebView2.CallDevToolsProtocolMethodAsync</c> and the protocol event
/// receiver. Keeping it as a tiny contract is the seam that lets the automation engine stay free of
/// any WebView types and fully unit-testable.
/// </summary>
public interface ICdpTransport
{
    /// <summary>Invokes a CDP method (e.g. <c>Page.navigate</c>) with a JSON parameter object and returns the raw JSON result.</summary>
    Task<string> Send(string method, string parametersJson, CancellationToken cancellationToken = default);

    /// <summary>Raised for every CDP protocol event the page emits (e.g. <c>Page.loadEventFired</c>).</summary>
    event EventHandler<CdpEvent>? EventReceived;
}

/// <summary>A CDP protocol event with its raw JSON parameters.</summary>
public sealed record CdpEvent(string Method, string ParametersJson);
