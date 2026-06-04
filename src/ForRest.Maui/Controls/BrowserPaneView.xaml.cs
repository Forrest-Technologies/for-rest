using ForRest.Browser;
using ForRest.Maui.Services.Browser;
using ForRest.Maui.ViewModels;
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
public partial class BrowserPaneView : ContentView
{
    #region Private Fields

    private readonly BrowserAutomationProvider? provider;
    private readonly ILogger<BrowserPaneView>? logger;
    private readonly BrowserRecordingScriptBuilder recordingBuilder = new();
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

    #region Private Methods

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
}
