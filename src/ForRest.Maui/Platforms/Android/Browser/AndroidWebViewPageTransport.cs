#if ANDROID
using System.Globalization;
using Android.Graphics;
using Android.OS;
using Android.Views;
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
///
/// It also implements <see cref="IBrowserTrustedInput"/>: taps are delivered as real
/// <see cref="MotionEvent"/>s through the WebView's own touch pipeline, so the page sees OS-trusted
/// (<c>event.isTrusted = true</c>) input rather than synthetic DOM events.
/// </summary>
internal sealed class AndroidWebViewPageTransport(AWebView webView) : IBrowserPageTransport, IBrowserTrustedInput
{
    #region Private Fields

    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);

    #endregion

    #region Properties

    public bool IsAvailable => true;

    public bool SupportsTrustedTap => true;

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

    public async Task<bool> TryTrustedTap(double cssX, double cssY, CancellationToken cancellationToken = default)
    {
        // Page rects are CSS pixels relative to the viewport; the WebView's touch pipeline wants physical
        // pixels relative to the view. devicePixelRatio is exactly that CSS->physical scale.
        double ratio = await GetDevicePixelRatio(cancellationToken);
        float deviceX = (float)(cssX * ratio);
        float deviceY = (float)(cssY * ratio);

        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                long downTime = SystemClock.UptimeMillis();

                // down -> a couple of tiny moves (so it reads as a finger, not a teleport) -> up, with
                // event-time offsets that keep the gesture inside tap (not long-press) timing.
                bool dispatched = DispatchTouch(MotionEventActions.Down, deviceX, deviceY, downTime, downTime);
                DispatchTouch(MotionEventActions.Move, deviceX + 0.6f, deviceY + 0.4f, downTime, downTime + 12);
                DispatchTouch(MotionEventActions.Up, deviceX, deviceY, downTime, downTime + 58);
                completion.TrySetResult(dispatched);
            }
            catch (Exception)
            {
                // Any failure falls back to the synthetic DOM click in the driver.
                completion.TrySetResult(false);
            }
        });

        return await completion.Task.WaitAsync(cancellationToken);
    }

    #endregion

    #region Helpers

    private bool DispatchTouch(MotionEventActions action, float x, float y, long downTime, long eventTime)
    {
        MotionEvent? motionEvent = MotionEvent.Obtain(downTime, eventTime, action, x, y, 0);
        if (motionEvent is null)
        {
            return false;
        }

        try
        {
            return webView.DispatchTouchEvent(motionEvent);
        }
        finally
        {
            motionEvent.Recycle();
        }
    }

    private async Task<double> GetDevicePixelRatio(CancellationToken cancellationToken)
    {
        try
        {
            string raw = await Evaluate("window.devicePixelRatio", cancellationToken);
            string trimmed = (raw ?? string.Empty).Trim().Trim('"');
            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio) && ratio > 0
                ? ratio
                : 1d;
        }
        catch (Exception)
        {
            return 1d;
        }
    }

    private sealed class JsValueCallback(TaskCompletionSource<string> completion)
        : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) =>
            completion.TrySetResult(value?.ToString() ?? "null");
    }

    #endregion
}
#endif
