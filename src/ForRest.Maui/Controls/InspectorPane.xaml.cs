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
	}

	private async void OnCopyRequestClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyRequestBodyAsync();
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
	}

	private async void OnCopyRawRequestClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyRawRequestAsync();
	}

	private async void OnCopyStashClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyStashAsync();
	}

	private async void OnCopySelectedStashClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopySelectedStashRowAsync();
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
	}

	private async void OnCopyTraceClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyTraceAsync();
	}

	private async void OnCopyDebugClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyDebugOutputAsync();
	}

	private async void OnExportStashClicked(object? sender, EventArgs e)
	{
		await ViewModel.ExportStashCsvAsync();
	}

	private void OnInlineCopyCompleted(object? sender, CopyableLabelCopiedEventArgs e)
	{
		ViewModel.ExecutionStatus = e.Message;
	}

	private async void OnResponseVarCopyRequested(object? sender, MonacoResponseVarRequestEventArgs e)
	{
		await ViewModel.CopyResponseVariableAsync(e.LineNumber, e.Column);
	}

	private void EnsureResponseBodyViewer()
	{
		if (ResponseBodyViewerHost.Content is not null || !IsVisible || !ViewModel.IsInspectorResponseVisible)
		{
			return;
		}

		ResponseBodyViewerHost.Content = PlatformExperience.UseWebCodeEditors() && !AppLaunchGuard.IsSafeModeEnabled
			? BuildMonacoResponseViewer()
			: BuildNativeResponseViewer();
	}

	private void EnsureRequestBodyViewer()
	{
		if (RequestBodyViewerHost.Content is not null || !IsVisible || !ViewModel.IsInspectorRequestVisible)
		{
			return;
		}

		RequestBodyViewerHost.Content = PlatformExperience.UseWebCodeEditors() && !AppLaunchGuard.IsSafeModeEnabled
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
