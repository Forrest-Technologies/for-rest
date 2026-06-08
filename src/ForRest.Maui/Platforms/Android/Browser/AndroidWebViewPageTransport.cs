#if ANDROID
using Android.Graphics;
using Android.Webkit;
using ForRest.Browser;
using Microsoft.Maui.ApplicationModel;
using AWebView = Android.Webkit.WebView;

namespace ForRest.Maui.Platforms.Android.Browser;

/// <summary>
/// Adapts the embedded Android <see cref="AWebView"/> to <see cref="IBrowserPageTransport"/> so the
/// shared <see cref="JsBridgeBrowserDriver"/> can drive it. It marshals every call onto the UI thread,
/// runs scripts through <c>WebView.EvaluateJavascript</c>, navigates with <c>LoadUrl</c> and waits for
/// <c>document.readyState</c> to settle, and captures a screenshot by drawing the view to a bitmap.
/// </summary>
internal sealed class AndroidWebViewPageTransport(AWebView webView) : IBrowserPageTransport
{
    #region Private Fields

    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);

    #endregion

    #region Properties

    public bool IsAvailable => true;

    #endregion

    #region Public Methods

    public Task<string> Evaluate(string script, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                webView.EvaluateJavascript(script, new JsValueCallback(completion));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        return completion.Task.WaitAsync(cancellationToken);
    }

    public async Task Navigate(string url, CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() => webView.LoadUrl(url));

        // Android's WebView has no load-completion awaitable here, so poll the document ready state until
        // it settles or the navigation budget elapses (mirrors the bounded wait the CDP client uses).
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(NavigationTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(200, cancellationToken);
            string state = await Evaluate("document.readyState", cancellationToken);
            if (state.Contains("complete", StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    public Task<string> CaptureScreenshot(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                int width = Math.Max(1, webView.Width);
                int height = Math.Max(1, webView.Height);
                using Bitmap bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
                using Canvas canvas = new(bitmap);
                webView.Draw(canvas);

                using MemoryStream stream = new();
                bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
                completion.TrySetResult(Convert.ToBase64String(stream.ToArray()));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        return completion.Task.WaitAsync(cancellationToken);
    }

    #endregion

    #region Helpers

    private sealed class JsValueCallback(TaskCompletionSource<string> completion)
        : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) =>
            completion.TrySetResult(value?.ToString() ?? "null");
    }

    #endregion
}
#endif
