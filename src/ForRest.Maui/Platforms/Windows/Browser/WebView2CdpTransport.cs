#if WINDOWS
using ForRest.Browser;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace ForRest.Maui.Platforms.Windows.Browser;

/// <summary>
/// Implements the engine's <see cref="ICdpTransport"/> over an embedded WebView2 by forwarding to
/// <see cref="CoreWebView2.CallDevToolsProtocolMethodAsync"/> and bridging protocol events. WebView2's
/// CDP surface must be touched on the UI thread, so every call is marshaled onto the WebView's
/// dispatcher while the automation engine itself runs on the thread pool.
/// </summary>
public sealed class WebView2CdpTransport(CoreWebView2 webView, DispatcherQueue dispatcher) : ICdpTransport
{
    #region Private Fields

    private readonly List<CoreWebView2DevToolsProtocolEventReceiver> receivers = [];

    #endregion

    #region Events

    public event EventHandler<CdpEvent>? EventReceived;

    #endregion

    #region Public Methods

    public Task<string> Send(string method, string parametersJson, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        string parameters = string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson;

        bool enqueued = dispatcher.TryEnqueue(async () =>
        {
            try
            {
                string result = await webView.CallDevToolsProtocolMethodAsync(method, parameters);
                completion.TrySetResult(result ?? "{}");
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        if (!enqueued)
        {
            completion.TrySetException(new InvalidOperationException("The browser UI dispatcher is unavailable."));
        }

        return completion.Task;
    }

    /// <summary>Subscribes to a CDP protocol event (e.g. <c>Page.loadEventFired</c>) and re-raises it.</summary>
    public void Subscribe(string eventName)
    {
        CoreWebView2DevToolsProtocolEventReceiver receiver = webView.GetDevToolsProtocolEventReceiver(eventName);
        receiver.DevToolsProtocolEventReceived += (_, args) =>
            EventReceived?.Invoke(this, new CdpEvent(eventName, args.ParameterObjectAsJson));
        receivers.Add(receiver);
    }

    #endregion
}
#endif
