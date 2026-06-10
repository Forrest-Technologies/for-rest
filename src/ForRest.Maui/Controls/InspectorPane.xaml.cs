using System.ComponentModel;
using ForRest.Maui.Services;
using ForRest.Maui.ViewModels;
#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NativeScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
using Windows.System;
#endif

namespace ForRest.Maui.Controls;

public partial class InspectorPane : ContentView
{
	private INotifyPropertyChanged? _viewModelNotifier;
	private const double ResponseSnapshotCardWidth = 156d;
	private const double ResponseSnapshotCardSpacing = 8d;
#if WINDOWS
	private NativeScrollViewer? _responseSnapshotRailNativeView;
	private NativeScrollViewer? _requestSnapshotRailNativeView;
#endif

	public InspectorPane()
	{
		InitializeComponent();
		Loaded += (_, _) =>
		{
			EnsureResponseBodyViewer();
			EnsureRequestBodyViewer();
			EnsureResponseSnapshotRailInteraction();
			EnsureRequestSnapshotRailInteraction();
			QueueScrollSelectedResponseSnapshotIntoView(animated: false);
			QueueScrollSelectedRequestSnapshotIntoView(animated: false);
		};
		ResponseSnapshotRail.HandlerChanged += (_, _) => EnsureResponseSnapshotRailInteraction();
		RequestSnapshotRail.HandlerChanged += (_, _) => EnsureRequestSnapshotRailInteraction();
	}

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	protected override void OnBindingContextChanged()
	{
		if (_viewModelNotifier is not null)
		{
			_viewModelNotifier.PropertyChanged -= OnViewModelPropertyChanged;
			_viewModelNotifier = null;
		}

		base.OnBindingContextChanged();

		if (BindingContext is INotifyPropertyChanged notifier)
		{
			_viewModelNotifier = notifier;
			_viewModelNotifier.PropertyChanged += OnViewModelPropertyChanged;
		}

		EnsureResponseBodyViewer();
		EnsureRequestBodyViewer();
		EnsureResponseSnapshotRailInteraction();
		EnsureRequestSnapshotRailInteraction();
		QueueScrollSelectedResponseSnapshotIntoView(animated: false);
		QueueScrollSelectedRequestSnapshotIntoView(animated: false);
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(e.PropertyName) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.IsInspectorResponseVisible), StringComparison.Ordinal))
		{
			EnsureResponseBodyViewer();
		}

		// When the selected response changes (re-run or a different snapshot), the view mode or media
		// type can change, so rebuild/tear down the rendered viewer instead of leaving a stale
		// image/PDF/HTML over the new body. ShowRenderedResponse is raised on every snapshot change.
		if (string.IsNullOrWhiteSpace(e.PropertyName) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.ShowRenderedResponse), StringComparison.Ordinal))
		{
			RefreshResponseBodyViewer();
		}

		if (string.IsNullOrWhiteSpace(e.PropertyName) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.IsInspectorRequestVisible), StringComparison.Ordinal))
		{
			EnsureRequestBodyViewer();
		}

		if (string.IsNullOrWhiteSpace(e.PropertyName) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.IsInspectorResponseVisible), StringComparison.Ordinal) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.ShowResponseSnapshotSelector), StringComparison.Ordinal) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.SelectedResponseSnapshotEntry), StringComparison.Ordinal))
		{
			QueueScrollSelectedResponseSnapshotIntoView(animated: false);
		}

		if (string.IsNullOrWhiteSpace(e.PropertyName) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.IsInspectorRequestVisible), StringComparison.Ordinal) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.ShowRequestSnapshotSelector), StringComparison.Ordinal) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.SelectedRequestSnapshotEntry), StringComparison.Ordinal))
		{
			QueueScrollSelectedRequestSnapshotIntoView(animated: false);
		}
	}

	private void OnHideClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleRightPane();
	}

	private void OnTabClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: PaneTabViewModel tab })
		{
			ViewModel.SelectRightPaneTab(tab);
			EnsureResponseBodyViewer();
			EnsureRequestBodyViewer();
		}
	}

	private void OnTogglePrettyPrintClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleResponsePrettyPrint();
	}

	private async void OnCopyResponseClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyResponseBodyAsync();
		if (ViewModel.CanCopyResponseBody)
		{
			await ShowCopiedStateAsync(sender);
		}
	}

	private async void OnCopyRequestClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyRequestBodyAsync();
		if (ViewModel.CanCopyRequestBody)
		{
			await ShowCopiedStateAsync(sender);
		}
	}

	private void OnPreviousResponseSnapshotClicked(object? sender, EventArgs e)
	{
		if (ViewModel.SelectPreviousResponseSnapshot())
		{
			FocusResponseSnapshotRail();
			QueueScrollSelectedResponseSnapshotIntoView(animated: true);
		}
	}

	private void OnNextResponseSnapshotClicked(object? sender, EventArgs e)
	{
		if (ViewModel.SelectNextResponseSnapshot())
		{
			FocusResponseSnapshotRail();
			QueueScrollSelectedResponseSnapshotIntoView(animated: true);
		}
	}

	private void OnPreviousRequestSnapshotClicked(object? sender, EventArgs e)
	{
		if (ViewModel.SelectPreviousRequestSnapshot())
		{
			FocusRequestSnapshotRail();
			QueueScrollSelectedRequestSnapshotIntoView(animated: true);
		}
	}

	private void OnNextRequestSnapshotClicked(object? sender, EventArgs e)
	{
		if (ViewModel.SelectNextRequestSnapshot())
		{
			FocusRequestSnapshotRail();
			QueueScrollSelectedRequestSnapshotIntoView(animated: true);
		}
	}

	private void OnResponseSnapshotTapped(object? sender, TappedEventArgs e)
	{
		if (sender is Border { BindingContext: ResponseSnapshotEntryViewModel entry })
		{
			ViewModel.SelectedResponseSnapshotEntry = entry;
			FocusResponseSnapshotRail();
			QueueScrollSelectedResponseSnapshotIntoView(animated: true);
		}
	}

	private void OnRequestSnapshotTapped(object? sender, TappedEventArgs e)
	{
		if (sender is Border { BindingContext: RequestSnapshotEntryViewModel entry })
		{
			ViewModel.SelectedRequestSnapshotEntry = entry;
			FocusRequestSnapshotRail();
			QueueScrollSelectedRequestSnapshotIntoView(animated: true);
		}
	}

	private async void OnCopyRawResponseClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyRawResponseAsync();
		if (ViewModel.CanCopyRawResponse)
		{
			await ShowCopiedStateAsync(sender);
		}
	}

	private async void OnCopyRawRequestClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyRawRequestAsync();
		if (ViewModel.CanCopyRawRequest)
		{
			await ShowCopiedStateAsync(sender);
		}
	}

	private async void OnCopyStashClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyStashAsync();
		if (ViewModel.CanCopyStash)
		{
			await ShowCopiedStateAsync(sender);
		}
	}

	private async void OnCopySelectedStashClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopySelectedStashRowAsync();
		if (ViewModel.CanCopySelectedStashRow)
		{
			await ShowCopiedStateAsync(sender);
		}
	}

	private void OnClearStashFilterClicked(object? sender, EventArgs e)
	{
		ViewModel.ClearStashFilter();
	}

	private void OnChangeStashSortClicked(object? sender, EventArgs e)
	{
		ViewModel.CycleStashSortMode();
	}

	private void OnToggleStashSortDirectionClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleStashSortDirection();
	}

	private void OnToggleHideEmptyStashColumnsClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleHideEmptyStashColumns();
	}

	private void OnStashRowTapped(object? sender, TappedEventArgs e)
	{
		if (sender is Border { BindingContext: StashRowViewModel row })
		{
			ViewModel.SelectStashRow(row);
		}
	}

	private async void OnCopyHeadersClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyHeadersAsync();
		if (ViewModel.CanCopyHeaders)
		{
			await ShowCopiedStateAsync(sender);
		}
	}

	private async void OnCopyTraceClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyTraceAsync();
		if (ViewModel.CanCopyTrace)
		{
			await ShowCopiedStateAsync(sender);
		}
	}

	private async void OnCopyDebugClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyDebugOutputAsync();
		if (ViewModel.CanCopyDebugOutput)
		{
			await ShowCopiedStateAsync(sender);
		}
	}

	private async void OnCopyDebugSummaryClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyDebugSummaryAsync();
		if (ViewModel.CanCopyDebugSummary)
		{
			await ShowCopiedStateAsync(sender);
		}
	}

	private static async Task ShowCopiedStateAsync(object? sender)
	{
		if (sender is not Button button)
		{
			return;
		}

		string originalText = button.Text;
		button.Text = "Copied";

		try
		{
			await Task.Delay(1200);
		}
		finally
		{
			button.Text = originalText;
		}
	}

	private async void OnExportStashClicked(object? sender, EventArgs e)
	{
		await ViewModel.ExportStashCsvAsync();
	}

	private async void OnSaveResponseBodyClicked(object? sender, EventArgs e)
	{
		await ViewModel.SaveResponseBodyAsync();
	}

	private async void OnSaveRawResponseClicked(object? sender, EventArgs e)
	{
		await ViewModel.SaveRawExchangeAsync();
	}

	private async void OnSaveHeadersClicked(object? sender, EventArgs e)
	{
		await ViewModel.SaveHeadersAsync();
	}

	private void OnInlineCopyCompleted(object? sender, CopyableLabelCopiedEventArgs e)
	{
		ViewModel.ExecutionStatus = e.Message;
	}

	private async void OnResponseVarCopyRequested(object? sender, EditorResponseVarRequestEventArgs e)
	{
		await ViewModel.CopyResponseVariableAsync(e.LineNumber, e.Column);
	}

	private void OnShowResponseRenderedClicked(object? sender, EventArgs e)
	{
		ViewModel.ShowResponseRendered();
		RefreshResponseBodyViewer();
	}

	private void OnShowResponseSourceClicked(object? sender, EventArgs e)
	{
		ViewModel.ShowResponseSource();
		RefreshResponseBodyViewer();
	}

	private void EnsureResponseBodyViewer()
	{
		if (ResponseBodyViewerHost.Content is not null || !IsVisible || !ViewModel.IsInspectorResponseVisible)
		{
			return;
		}

		ResponseBodyViewerHost.Content = BuildResponseBodyViewer();
	}

	private void RefreshResponseBodyViewer()
	{
		if (!IsVisible || !ViewModel.IsInspectorResponseVisible)
		{
			return;
		}

		bool needsRenderedViewer = ViewModel.ShowRenderedResponse;
		bool hasRenderedViewer = ResponseBodyViewerHost.Content is WebView or Image;

		// The rendered view (HTML page, PDF, or image) is rebuilt whenever it is active, since the
		// content and media type can change between responses.
		if (needsRenderedViewer)
		{
			ResponseBodyViewerHost.Content = BuildResponseBodyViewer();
			return;
		}

		if (!hasRenderedViewer && ResponseBodyViewerHost.Content is not null)
		{
			return;
		}

		ResponseBodyViewerHost.Content = BuildResponseBodyViewer();
	}

	private View BuildResponseBodyViewer()
	{
		if (ViewModel.ShowRenderedResponse)
		{
			return BuildRenderedResponseViewer();
		}

		if (AppLaunchGuard.IsSafeModeEnabled)
		{
			return BuildNativeResponseViewer();
		}

		if (PlatformExperience.UseSoraEditor())
		{
			return BuildSoraResponseViewer();
		}

		return PlatformExperience.UseWebCodeEditors()
			? BuildMonacoResponseViewer()
			: BuildNativeResponseViewer();
	}

	private View BuildRenderedResponseViewer()
	{
		// Images render in a native Image control from the captured bytes.
		if (ViewModel.IsImageResponse)
		{
			byte[]? bytes = ViewModel.ResponseBinaryContent;
			if (bytes is not null)
			{
				return new Image
				{
					Source = ImageSource.FromStream(() => new MemoryStream(bytes)),
					Aspect = Aspect.AspectFit,
					VerticalOptions = LayoutOptions.Fill,
					HorizontalOptions = LayoutOptions.Fill,
				};
			}
		}

		WebView webView = new()
		{
			VerticalOptions = LayoutOptions.Fill,
			HorizontalOptions = LayoutOptions.Fill,
		};

		if (ViewModel.IsPdfResponse)
		{
			// Chromium (WebView2 on desktop) renders a PDF data URL inline.
			webView.Source = new UrlWebViewSource { Url = ViewModel.ResponsePdfDataUrl };
		}
		else
		{
			webView.Source = new HtmlWebViewSource { Html = ViewModel.ResponseBodyText ?? string.Empty };
		}

		return webView;
	}

	private void EnsureRequestBodyViewer()
	{
		if (RequestBodyViewerHost.Content is not null || !IsVisible || !ViewModel.IsInspectorRequestVisible)
		{
			return;
		}

		RequestBodyViewerHost.Content = BuildRequestBodyViewer();
	}

	private View BuildRequestBodyViewer()
	{
		if (AppLaunchGuard.IsSafeModeEnabled)
		{
			return BuildNativeRequestViewer();
		}

		if (PlatformExperience.UseSoraEditor())
		{
			return BuildSoraRequestViewer();
		}

		return PlatformExperience.UseWebCodeEditors()
			? BuildMonacoRequestViewer()
			: BuildNativeRequestViewer();
	}

	protected override void OnPropertyChanged(string? propertyName = null)
	{
		base.OnPropertyChanged(propertyName);

		if (string.Equals(propertyName, nameof(IsVisible), StringComparison.Ordinal) && IsVisible)
		{
			EnsureResponseBodyViewer();
			EnsureRequestBodyViewer();
			EnsureResponseSnapshotRailInteraction();
			EnsureRequestSnapshotRailInteraction();
			QueueScrollSelectedResponseSnapshotIntoView(animated: false);
			QueueScrollSelectedRequestSnapshotIntoView(animated: false);
		}
	}

	private void EnsureResponseSnapshotRailInteraction()
	{
#if WINDOWS
		NativeScrollViewer? platformView = ResponseSnapshotRail.Handler?.PlatformView as NativeScrollViewer;
		if (ReferenceEquals(_responseSnapshotRailNativeView, platformView))
		{
			return;
		}

		if (_responseSnapshotRailNativeView is not null)
		{
			_responseSnapshotRailNativeView.PointerWheelChanged -= OnResponseSnapshotRailPointerWheelChanged;
			_responseSnapshotRailNativeView.KeyDown -= OnResponseSnapshotRailKeyDown;
		}

		_responseSnapshotRailNativeView = platformView;
		if (_responseSnapshotRailNativeView is not null)
		{
			_responseSnapshotRailNativeView.IsTabStop = true;
			_responseSnapshotRailNativeView.PointerWheelChanged += OnResponseSnapshotRailPointerWheelChanged;
			_responseSnapshotRailNativeView.KeyDown += OnResponseSnapshotRailKeyDown;
		}
#endif
	}

	private void EnsureRequestSnapshotRailInteraction()
	{
#if WINDOWS
		NativeScrollViewer? platformView = RequestSnapshotRail.Handler?.PlatformView as NativeScrollViewer;
		if (ReferenceEquals(_requestSnapshotRailNativeView, platformView))
		{
			return;
		}

		if (_requestSnapshotRailNativeView is not null)
		{
			_requestSnapshotRailNativeView.PointerWheelChanged -= OnRequestSnapshotRailPointerWheelChanged;
			_requestSnapshotRailNativeView.KeyDown -= OnRequestSnapshotRailKeyDown;
		}

		_requestSnapshotRailNativeView = platformView;
		if (_requestSnapshotRailNativeView is not null)
		{
			_requestSnapshotRailNativeView.IsTabStop = true;
			_requestSnapshotRailNativeView.PointerWheelChanged += OnRequestSnapshotRailPointerWheelChanged;
			_requestSnapshotRailNativeView.KeyDown += OnRequestSnapshotRailKeyDown;
		}
#endif
	}

	private void FocusResponseSnapshotRail()
	{
#if WINDOWS
		if (_responseSnapshotRailNativeView is not null)
		{
			_responseSnapshotRailNativeView.Focus(FocusState.Programmatic);
			return;
		}
#endif
		ResponseSnapshotRail.Focus();
	}

	private void FocusRequestSnapshotRail()
	{
#if WINDOWS
		if (_requestSnapshotRailNativeView is not null)
		{
			_requestSnapshotRailNativeView.Focus(FocusState.Programmatic);
			return;
		}
#endif
		RequestSnapshotRail.Focus();
	}

	private void QueueScrollSelectedResponseSnapshotIntoView(bool animated)
	{
		if (!IsVisible || !ViewModel.ShowResponseSnapshotSelector || ViewModel.SelectedResponseSnapshotEntry is null)
		{
			return;
		}

		if (Dispatcher?.IsDispatchRequired == true)
		{
			Dispatcher.Dispatch(() => _ = ScrollSelectedResponseSnapshotIntoViewAsync(animated));
			return;
		}

		_ = ScrollSelectedResponseSnapshotIntoViewAsync(animated);
	}

	private void QueueScrollSelectedRequestSnapshotIntoView(bool animated)
	{
		if (!IsVisible || !ViewModel.ShowRequestSnapshotSelector || ViewModel.SelectedRequestSnapshotEntry is null)
		{
			return;
		}

		if (Dispatcher?.IsDispatchRequired == true)
		{
			Dispatcher.Dispatch(() => _ = ScrollSelectedRequestSnapshotIntoViewAsync(animated));
			return;
		}

		_ = ScrollSelectedRequestSnapshotIntoViewAsync(animated);
	}

	private async Task ScrollSelectedResponseSnapshotIntoViewAsync(bool animated)
	{
		if (ViewModel.SelectedResponseSnapshotEntry is not { } selectedEntry ||
		    !ViewModel.ShowResponseSnapshotSelector ||
		    ResponseSnapshotRail.Width <= 0d)
		{
			return;
		}

		await Task.Yield();
		int selectedIndex = ViewModel.ResponseSnapshotEntries.IndexOf(selectedEntry);
		if (selectedIndex < 0)
		{
			return;
		}

		double targetOffset = Math.Max(0d, (selectedIndex * (ResponseSnapshotCardWidth + ResponseSnapshotCardSpacing)) - ResponseSnapshotCardSpacing);
		await ResponseSnapshotRail.ScrollToAsync(targetOffset, 0d, animated);
	}

	private async Task ScrollSelectedRequestSnapshotIntoViewAsync(bool animated)
	{
		if (ViewModel.SelectedRequestSnapshotEntry is not { } selectedEntry ||
		    !ViewModel.ShowRequestSnapshotSelector ||
		    RequestSnapshotRail.Width <= 0d)
		{
			return;
		}

		await Task.Yield();
		int selectedIndex = ViewModel.RequestSnapshotEntries.IndexOf(selectedEntry);
		if (selectedIndex < 0)
		{
			return;
		}

		double targetOffset = Math.Max(0d, (selectedIndex * (ResponseSnapshotCardWidth + ResponseSnapshotCardSpacing)) - ResponseSnapshotCardSpacing);
		await RequestSnapshotRail.ScrollToAsync(targetOffset, 0d, animated);
	}

	private View BuildMonacoResponseViewer()
	{
		MonacoEditorSurface viewer = new()
		{
			Language = "json",
			IsReadOnly = true,
			EnableResponseActions = true
		};
		viewer.SetBinding(MonacoEditorSurface.ThemeKeyProperty, nameof(MainPageViewModel.EditorThemeKey));
		viewer.SetBinding(MonacoEditorSurface.TextProperty, nameof(MainPageViewModel.ResponseBodyText));
		viewer.ResponseVarCopyRequested += OnResponseVarCopyRequested;
		return viewer;
	}

	private View BuildSoraResponseViewer()
	{
		SoraEditorSurface viewer = new()
		{
			Language = "json",
			IsReadOnly = true,
			EnableResponseActions = true,
		};
		viewer.SetBinding(SoraEditorSurface.ThemeKeyProperty, nameof(MainPageViewModel.EditorThemeKey));
		viewer.SetBinding(SoraEditorSurface.TextProperty, nameof(MainPageViewModel.ResponseBodyText));
		viewer.ResponseVarCopyRequested += OnResponseVarCopyRequested;
		return viewer;
	}

	private View BuildNativeResponseViewer()
	{
		EditorSurface viewer = new()
		{
			Language = "json",
			IsReadOnly = true,
			ShowHeader = false,
			ShowFooter = false
		};
		viewer.SetBinding(EditorSurface.TextProperty, nameof(MainPageViewModel.ResponseBodyText));
		return viewer;
	}

	private View BuildMonacoRequestViewer()
	{
		MonacoEditorSurface viewer = new()
		{
			Language = "json",
			IsReadOnly = true,
		};
		viewer.SetBinding(MonacoEditorSurface.ThemeKeyProperty, nameof(MainPageViewModel.EditorThemeKey));
		viewer.SetBinding(MonacoEditorSurface.TextProperty, nameof(MainPageViewModel.RequestBodyText));
		return viewer;
	}

	private View BuildSoraRequestViewer()
	{
		SoraEditorSurface viewer = new()
		{
			Language = "json",
			IsReadOnly = true,
		};
		viewer.SetBinding(SoraEditorSurface.ThemeKeyProperty, nameof(MainPageViewModel.EditorThemeKey));
		viewer.SetBinding(SoraEditorSurface.TextProperty, nameof(MainPageViewModel.RequestBodyText));
		return viewer;
	}

	private View BuildNativeRequestViewer()
	{
		EditorSurface viewer = new()
		{
			Language = "json",
			IsReadOnly = true,
			ShowHeader = false,
			ShowFooter = false
		};
		viewer.SetBinding(EditorSurface.TextProperty, nameof(MainPageViewModel.RequestBodyText));
		return viewer;
	}

#if WINDOWS
	private void OnResponseSnapshotRailKeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (!ViewModel.ShowResponseSnapshotSelector)
		{
			return;
		}

		bool handled = e.Key switch
		{
			VirtualKey.Left => ViewModel.SelectPreviousResponseSnapshot(),
			VirtualKey.Right => ViewModel.SelectNextResponseSnapshot(),
			_ => false,
		};

		if (!handled)
		{
			return;
		}

		QueueScrollSelectedResponseSnapshotIntoView(animated: true);
		e.Handled = true;
	}

	private void OnResponseSnapshotRailPointerWheelChanged(object sender, PointerRoutedEventArgs e)
	{
		if (sender is not NativeScrollViewer platformView ||
		    !ViewModel.ShowResponseSnapshotSelector ||
		    platformView.ScrollableWidth <= 0d)
		{
			return;
		}

		int wheelDelta = e.GetCurrentPoint(platformView).Properties.MouseWheelDelta;
		if (wheelDelta == 0)
		{
			return;
		}

		double step = Math.Max(ResponseSnapshotCardWidth + ResponseSnapshotCardSpacing, platformView.ViewportWidth * 0.72d);
		double targetOffset = Math.Clamp(
			platformView.HorizontalOffset - (Math.Sign(wheelDelta) * step),
			0d,
			platformView.ScrollableWidth);
		if (Math.Abs(targetOffset - platformView.HorizontalOffset) < 0.5d)
		{
			return;
		}

		platformView.ChangeView(targetOffset, null, null, true);
		e.Handled = true;
	}

	private void OnRequestSnapshotRailKeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (!ViewModel.ShowRequestSnapshotSelector)
		{
			return;
		}

		bool handled = e.Key switch
		{
			VirtualKey.Left => ViewModel.SelectPreviousRequestSnapshot(),
			VirtualKey.Right => ViewModel.SelectNextRequestSnapshot(),
			_ => false,
		};

		if (!handled)
		{
			return;
		}

		QueueScrollSelectedRequestSnapshotIntoView(animated: true);
		e.Handled = true;
	}

	private void OnRequestSnapshotRailPointerWheelChanged(object sender, PointerRoutedEventArgs e)
	{
		if (sender is not NativeScrollViewer platformView ||
		    !ViewModel.ShowRequestSnapshotSelector ||
		    platformView.ScrollableWidth <= 0d)
		{
			return;
		}

		int wheelDelta = e.GetCurrentPoint(platformView).Properties.MouseWheelDelta;
		if (wheelDelta == 0)
		{
			return;
		}

		double step = Math.Max(ResponseSnapshotCardWidth + ResponseSnapshotCardSpacing, platformView.ViewportWidth * 0.72d);
		double targetOffset = Math.Clamp(
			platformView.HorizontalOffset - (Math.Sign(wheelDelta) * step),
			0d,
			platformView.ScrollableWidth);
		if (Math.Abs(targetOffset - platformView.HorizontalOffset) < 0.5d)
		{
			return;
		}

		platformView.ChangeView(targetOffset, null, null, true);
		e.Handled = true;
	}
#endif
}
