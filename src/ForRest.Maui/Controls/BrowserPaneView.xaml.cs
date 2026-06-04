using ForRest.Browser;
using ForRest.Maui.Services.Browser;
using ForRest.Maui.ViewModels;
using ForRest.Services.AI.OAuth;
using Microsoft.Extensions.Logging;
#if WINDOWS
using ForRest.Maui.Platforms.Windows.Browser;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using WinWebView2 = Microsoft.UI.Xaml.Controls.WebView2;
#endif

namespace ForRest.Maui.Controls;

/// <summary>
/// The live browser pane: a dense IDE-style chrome strip (back/forward/reload, address, go, a record
/// toggle, and a "take over" toggle that suspends synthetic input) above an embedded <see cref="WebView"/>.
/// On Windows it publishes the WebView2 to the app's <see cref="BrowserAutomationProvider"/> so the
/// scripting <c>browser.*</c> API and MCP <c>browser_*</c> tools drive the visible page, and it records the
/// user's actions into a runnable <c>.frs</c> document. On other platforms the provider stays on its no-op
/// bridge for now (see the platform guards below).
/// </summary>
public partial class BrowserPaneView : ContentView, IInAppOAuthBrowser
{
    #region Private Fields

    private static readonly TimeSpan OAuthSignInTimeout = TimeSpan.FromMinutes(5);

    private readonly BrowserAutomationProvider? provider;
    private readonly InAppOAuthBrowserProvider? oauthBrowserProvider;
    private readonly ILogger<BrowserPaneView>? logger;
    private readonly BrowserRecordingScriptBuilder recordingBuilder = new();
    private OAuthInterception? oauthInterception;
    private bool isRecording;
    private bool isTakenOver;
    private bool isBridgeConnected;
#if WINDOWS
    private CoreWebView2? coreWebView;
    private DispatcherQueue? dispatcher;
    private bool isCoreInitializing;
#endif

    #endregion

    #region Constructors

    public BrowserPaneView()
    {
        InitializeComponent();

        IServiceProvider? services = IPlatformApplication.Current?.Services;
        provider = services?.GetService(typeof(BrowserAutomationProvider)) as BrowserAutomationProvider;
        oauthBrowserProvider = services?.GetService(typeof(InAppOAuthBrowserProvider)) as InAppOAuthBrowserProvider;
        logger = services?.GetService(typeof(ILogger<BrowserPaneView>)) as ILogger<BrowserPaneView>;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #endregion

    #region Properties

    private MainPageViewModel? ViewModel => BindingContext as MainPageViewModel;

    #endregion

    #region Event Handlers

    private void OnLoaded(object? sender, EventArgs e)
    {
        SetStatus("Enter an address and press Go to load a page.");
        PageWebView.Navigating += OnWebViewNavigating;
        oauthBrowserProvider?.Connect(this);
#if WINDOWS
        PageWebView.HandlerChanged += OnWebViewHandlerChanged;
        TryInitializeCoreWebView();
#else
        // TODO: connect a JavaScript-bridge browser bridge on Android/macCatalyst so scripts and MCP can
        // drive the embedded WebView there too. Until then the provider stays on its no-op bridge.
        logger?.LogInformation("Browser pane live bridge is only wired on Windows; provider remains on the null bridge.");
#endif
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        DisconnectBridge();
        PageWebView.Navigating -= OnWebViewNavigating;
        oauthBrowserProvider?.Disconnect(this);

        // A teardown mid-sign-in must not leave the awaiting run hanging forever.
        oauthInterception?.Cancel(new OperationCanceledException("The browser pane closed during OAuth sign-in."));
#if WINDOWS
        PageWebView.HandlerChanged -= OnWebViewHandlerChanged;
#endif
    }

    private void OnBackClicked(object? sender, EventArgs e)
    {
        if (PageWebView.CanGoBack)
        {
            PageWebView.GoBack();
        }
    }

    private void OnForwardClicked(object? sender, EventArgs e)
    {
        if (PageWebView.CanGoForward)
        {
            PageWebView.GoForward();
        }
    }

    private void OnReloadClicked(object? sender, EventArgs e)
    {
        PageWebView.Reload();
    }

    private void OnGoClicked(object? sender, EventArgs e)
    {
        Navigate(AddressEntry.Text);
    }

    private void OnAddressCompleted(object? sender, EventArgs e)
    {
        Navigate(AddressEntry.Text);
    }

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        OAuthInterception? interception = oauthInterception;
        if (interception is null)
        {
            return;
        }

        if (!RedirectUriMatcher.IsRedirect(e.Url, interception.RedirectUri))
        {
            return;
        }

        // Stop the WebView before it actually loads the redirect target (which may be an opaque provider
        // callback page), then hand the code back to the awaiting run.
        e.Cancel = true;
        interception.TryComplete(e.Url);
    }

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(e.Url))
        {
            AddressEntry.Text = e.Url;
            if (isRecording)
            {
                recordingBuilder.AddNavigation(e.Url);
            }
        }

#if WINDOWS
        if (isRecording)
        {
            _ = InjectRecorderScript();
        }
#endif

        RefreshStatus();
    }

    private void OnRecordToggleClicked(object? sender, EventArgs e)
    {
        if (isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void OnTakeOverToggleClicked(object? sender, EventArgs e)
    {
        isTakenOver = !isTakenOver;
        TakeOverButton.Text = isTakenOver ? "Resume bot" : "Take over";

        if (isTakenOver)
        {
            // Suspend synthetic input: drop the bridge so scripts/MCP cannot inject CDP input while the
            // user drives the page directly. Recording (which only observes real user actions) continues.
            provider?.Disconnect();
            isBridgeConnected = false;
        }
        else
        {
            ConnectBridge();
        }

        RefreshStatus();
    }

    #endregion

    #region Interface Implementations

    public async Task<string> Authorize(string authorizeUrl, string redirectUri, string state, CancellationToken cancellationToken)
    {
        if (oauthInterception is not null)
        {
            throw new InvalidOperationException("An OAuth sign-in is already in progress in the browser pane.");
        }

        // Save the current UX state so we can put the user back exactly where they were afterward.
        PaneTabViewModel? previousCenterTab = ViewModel?.CenterTabs.FirstOrDefault(static tab => tab.IsSelected);

        OAuthInterception interception = new(redirectUri, state);
        oauthInterception = interception;

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OAuthSignInTimeout);
        using CancellationTokenRegistration registration = timeout.Token.Register(
            () => interception.Cancel(
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                    ? new TimeoutException($"Browser sign-in timed out after {OAuthSignInTimeout.TotalMinutes:0} minutes without a redirect. Try again.")
                    : new OperationCanceledException(cancellationToken)));

        try
        {
            await InvokeOnUiThreadAsync(() =>
            {
                ShowBrowserPane();
                SetStatus("Waiting for sign-in to complete in the browser pane...");
                PageWebView.Source = authorizeUrl;
            });

            return await interception.Task;
        }
        finally
        {
            oauthInterception = null;

            // Restore the previous UX state regardless of success, cancellation, or failure.
            await InvokeOnUiThreadAsync(() =>
            {
                if (previousCenterTab is not null)
                {
                    ViewModel?.SelectCenterTab(previousCenterTab);
                }

                RefreshStatus();
            });
        }
    }

    #endregion

    #region Private Methods

    private void ShowBrowserPane()
    {
        PaneTabViewModel? browserTab = ViewModel?.CenterTabs.FirstOrDefault(
            static tab => string.Equals(tab.Key, "browser", StringComparison.OrdinalIgnoreCase));
        if (browserTab is not null)
        {
            ViewModel?.SelectCenterTab(browserTab);
        }
    }

    private static Task InvokeOnUiThreadAsync(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(action);
    }

    private void Navigate(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        string url = NormalizeUrl(address.Trim());
        try
        {
            PageWebView.Source = url;
            if (isRecording)
            {
                recordingBuilder.AddNavigation(url);
            }
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Failed to navigate the browser pane to {Url}.", url);
            SetStatus($"Could not load {url}.");
        }
    }

    private static string NormalizeUrl(string address)
    {
        if (address.Contains("://", StringComparison.Ordinal))
        {
            return address;
        }

        return $"https://{address}";
    }

    private void StartRecording()
    {
        isRecording = true;
        RecordButton.Text = "Stop";
        if (PageWebView.Source is UrlWebViewSource source && !string.IsNullOrWhiteSpace(source.Url))
        {
            recordingBuilder.AddNavigation(source.Url);
        }

#if WINDOWS
        _ = InjectRecorderScript();
#else
        // TODO: install the recorder content script via the platform WebView's JS bridge on Android/macOS.
        logger?.LogInformation("Browser recording capture is only wired on Windows in this build.");
#endif
        RefreshStatus();
    }

    private void StopRecording()
    {
        isRecording = false;
        RecordButton.Text = "Record";

#if WINDOWS
        if (coreWebView is not null)
        {
            try
            {
                _ = coreWebView.ExecuteScriptAsync(BrowserRecorderJs.Stop);
            }
            catch (Exception exception)
            {
                logger?.LogWarning(exception, "Failed to stop the in-page recorder script.");
            }
        }
#endif

        string script = recordingBuilder.Build();
        string? location = ViewModel?.CreateBrowserRecordingDocument(script);
        SetStatus(location is null
            ? "Recording stopped. Nothing was captured."
            : $"Recording saved to {location}.");
    }

    private void ConnectBridge()
    {
#if WINDOWS
        if (provider is null || coreWebView is null || dispatcher is null)
        {
            return;
        }

        _ = ConnectBridgeAsync();
#endif
    }

#if WINDOWS
    private async Task ConnectBridgeAsync()
    {
        try
        {
            await WebView2BrowserConnector.Connect(coreWebView!, dispatcher!, provider!);
            isBridgeConnected = true;
            RefreshStatus();
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Failed to connect the live browser bridge.");
            SetStatus("Failed to connect the automation bridge.");
        }
    }
#endif

    private void DisconnectBridge()
    {
        if (isBridgeConnected)
        {
            provider?.Disconnect();
            isBridgeConnected = false;
        }
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }

    private void RefreshStatus()
    {
        if (isRecording)
        {
            SetStatus($"Recording {recordingBuilder.LineCount} step(s). Interact with the page, then press Stop.");
            return;
        }

        if (isTakenOver)
        {
            SetStatus("Take over active. Synthetic input is suspended; scripts and agents are paused.");
            return;
        }

        SetStatus(isBridgeConnected
            ? "Live. Scripts and agents drive this page."
            : "Enter an address and press Go to load a page.");
    }

#if WINDOWS
    private void OnWebViewHandlerChanged(object? sender, EventArgs e)
    {
        TryInitializeCoreWebView();
    }

    private void TryInitializeCoreWebView()
    {
        if (coreWebView is not null || isCoreInitializing)
        {
            return;
        }

        if (PageWebView.Handler?.PlatformView is not WinWebView2 platformWebView)
        {
            return;
        }

        isCoreInitializing = true;
        dispatcher = platformWebView.DispatcherQueue;

        if (platformWebView.CoreWebView2 is not null)
        {
            OnCoreWebViewReady(platformWebView.CoreWebView2);
            return;
        }

        platformWebView.CoreWebView2Initialized += (_, _) =>
        {
            if (platformWebView.CoreWebView2 is not null)
            {
                OnCoreWebViewReady(platformWebView.CoreWebView2);
            }
        };

        _ = EnsureCoreWebView2Async(platformWebView);
    }

    private async Task EnsureCoreWebView2Async(WinWebView2 platformWebView)
    {
        try
        {
            await platformWebView.EnsureCoreWebView2Async();
        }
        catch (Exception exception)
        {
            isCoreInitializing = false;
            logger?.LogError(exception, "Failed to initialize the embedded WebView2 for the browser pane.");
        }
    }

    private void OnCoreWebViewReady(CoreWebView2 core)
    {
        if (coreWebView is not null)
        {
            return;
        }

        coreWebView = core;
        isCoreInitializing = false;
        core.WebMessageReceived += OnCoreWebMessageReceived;

        if (!isTakenOver)
        {
            ConnectBridge();
        }
    }

    private void OnCoreWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!isRecording)
        {
            return;
        }

        string payload;
        try
        {
            payload = args.TryGetWebMessageAsString();
        }
        catch (Exception)
        {
            payload = args.WebMessageAsJson;
        }

        if (recordingBuilder.AddEventJson(payload))
        {
            Dispatcher.Dispatch(RefreshStatus);
        }
    }

    private async Task InjectRecorderScript()
    {
        if (coreWebView is null)
        {
            return;
        }

        try
        {
            string bootstrap = BrowserRecorderJs.Build("window.chrome.webview.postMessage(__frPayload)");
            await coreWebView.ExecuteScriptAsync(bootstrap);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Failed to inject the browser recorder script.");
        }
    }
#endif

    #endregion

    #region Helpers

    /// <summary>
    /// Tracks one in-flight OAuth sign-in: the redirect URI being awaited and a
    /// <see cref="TaskCompletionSource{TResult}"/> the navigation interceptor completes with the
    /// authorization code (or faults on cancellation, timeout, or a provider-reported error).
    /// </summary>
    private sealed class OAuthInterception(string redirectUri, string state)
    {
        private readonly TaskCompletionSource<string> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly string expectedState = state;

        public string RedirectUri { get; } = redirectUri;

        public Task<string> Task => completion.Task;

        public void TryComplete(string redirectUrl)
        {
            try
            {
                OAuthRedirectResult result = OAuthRedirectParser.ParseAndValidate(redirectUrl, expectedState);
                completion.TrySetResult(result.Code);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        public void Cancel(Exception exception) => completion.TrySetException(exception);
    }

    #endregion
}
