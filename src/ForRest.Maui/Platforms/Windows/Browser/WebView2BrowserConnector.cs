#if WINDOWS
using ForRest.Browser;
using ForRest.Maui.Services.Browser;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace ForRest.Maui.Platforms.Windows.Browser;

/// <summary>
/// Bridges a ready WebView2 to the app's <see cref="BrowserAutomationProvider"/>: builds the CDP
/// transport and driver, enables the protocol domains, and publishes the driver so scripts and MCP
/// tools can drive the page. The browser pane calls this once its <see cref="CoreWebView2"/> is created.
/// </summary>
public static class WebView2BrowserConnector
{
    public static async Task Connect(CoreWebView2 webView, DispatcherQueue dispatcher, BrowserAutomationProvider provider)
    {
        WebView2CdpTransport transport = new(webView, dispatcher);
        CdpClient client = new(transport);
        await client.EnableDomains();
        provider.Connect(new CdpBrowserDriver(client));
    }
}
#endif
