#if MACCATALYST
namespace ForRest.Maui.Platforms.MacCatalyst.Browser;

public static class MacCatalystBrowserConnector
{
    #region Public Methods

    public static void Connect(WebView webView, BrowserAutomationProvider provider, ILoggerFactory? loggerFactory)
    {
        var logger = (loggerFactory ?? NullLoggerFactory.Instance)
            .CreateLogger<MacCatalystWebViewPageTransport>();
        provider.Connect(new JsBridgeBrowserDriver(new MacCatalystWebViewPageTransport(webView, logger)));
    }

    #endregion
}
#endif
