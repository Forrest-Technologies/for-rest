#if MACCATALYST
namespace ForRest.Maui.Platforms.MacCatalyst.Browser;

internal sealed class MacCatalystWebViewPageTransport(
    WebView webView,
    ILogger<MacCatalystWebViewPageTransport> logger) : IBrowserPageTransport
{
    #region Private Fields

    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim navigationGate = new(1, 1);

    #endregion

    #region Properties

    public bool IsAvailable => webView.Handler?.PlatformView is WKWebView;

    #endregion

    #region Public Methods

    public Task<string> Evaluate(string script, CancellationToken cancellationToken = default)
    {
        return OnMainThread(async () =>
        {
            var expression = $"JSON.stringify((function() {{ return {script}; }})()) ?? 'null'";
            var platformWebView = (WKWebView)webView.Handler!.PlatformView!;
            using var result = await platformWebView.EvaluateJavaScriptAsync(expression);
            return result?.ToString() ?? "null";
        }, cancellationToken);
    }

    public async Task Navigate(string url, CancellationToken cancellationToken = default)
    {
        await navigationGate.WaitAsync(cancellationToken);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<WebNavigatedEventArgs> onNavigated = (_, args) =>
        {
            if (args.Result == WebNavigationResult.Success)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(new InvalidOperationException($"Browser navigation failed ({args.Result})."));
            }
        };
        try
        {
            await OnMainThread(() =>
            {
                webView.Navigated += onNavigated;
                webView.Source = new UrlWebViewSource { Url = url };
                return Task.FromResult(true);
            }, cancellationToken);
            await completion.Task.WaitAsync(NavigationTimeout, cancellationToken);
        }
        finally
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() => webView.Navigated -= onNavigated);
            }
            finally
            {
                navigationGate.Release();
            }
        }
    }

    public Task<string> CaptureScreenshot(CancellationToken cancellationToken = default)
    {
        return OnMainThread(async () =>
        {
            var platformWebView = (WKWebView)webView.Handler!.PlatformView!;
            using var configuration = new WKSnapshotConfiguration { AfterScreenUpdates = true };
            using var image = await platformWebView.TakeSnapshotAsync(configuration);
            using var png = image.AsPNG()
                ?? throw new InvalidOperationException("The browser could not produce a PNG screenshot.");
            return Convert.ToBase64String(png.ToArray());
        }, cancellationToken);
    }

    #endregion

    #region Private Methods

    private async Task<T> OnMainThread<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsAvailable)
                {
                    throw new InvalidOperationException("The browser WebView is not connected.");
                }

                return operation();
            }).WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Mac Catalyst browser operation failed.");
            throw;
        }
    }

    #endregion
}
#endif
