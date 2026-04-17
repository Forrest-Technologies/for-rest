using Android.Webkit;
using Microsoft.Maui.Handlers;

namespace ForRest.Maui.Platforms.Android.Handlers;

public class ForRestWebViewHandler : WebViewHandler
{
    #region Public Methods

    protected override global::Android.Webkit.WebView CreatePlatformView()
    {
        global::Android.Webkit.WebView webView = base.CreatePlatformView();
        WebSettings? settings = webView.Settings;
        if (settings is not null)
        {
            settings.JavaScriptEnabled = true;
            settings.DomStorageEnabled = true;
#pragma warning disable CA1422
            settings.AllowFileAccess = true;
            settings.AllowFileAccessFromFileURLs = true;
            settings.AllowUniversalAccessFromFileURLs = true;
#pragma warning restore CA1422
        }

        return webView;
    }

    #endregion
}
