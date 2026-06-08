#if ANDROID
using ForRest.Browser;
using ForRest.Maui.Services.Browser;
using AWebView = Android.Webkit.WebView;

namespace ForRest.Maui.Platforms.Android.Browser;

/// <summary>
/// Bridges a ready Android <see cref="AWebView"/> to the app's <see cref="BrowserAutomationProvider"/>:
/// builds the JavaScript-bridge transport and driver and publishes them so the scripting
/// <c>browser.*</c> API and MCP <c>browser_*</c> tools can drive the visible page on Android, the same
/// way <c>WebView2BrowserConnector</c> does on Windows. The browser pane calls this once its embedded
/// WebView's platform view exists.
/// </summary>
public static class AndroidBrowserConnector
{
    public static void Connect(AWebView webView, BrowserAutomationProvider provider)
    {
        AndroidWebViewPageTransport transport = new(webView);
        provider.Connect(new JsBridgeBrowserDriver(transport));
    }
}
#endif
