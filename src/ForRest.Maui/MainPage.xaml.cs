using ForRest.Maui.ViewModels;
#if WINDOWS
using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
#endif

namespace ForRest.Maui;

public partial class MainPage : ContentPage
{
	private double _leftPaneWidthOnDragStart;
	private double _rightPaneWidthOnDragStart;
	private bool _isInitialized;
#if WINDOWS
	private UIElement? _leftSplitterNativeView;
	private UIElement? _rightSplitterNativeView;
	private FrameworkElement? _pageNativeView;
	private static readonly PropertyInfo? ProtectedCursorProperty = typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly Microsoft.UI.Input.InputSystemCursor ResizeCursor =
		Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
#endif

	public MainPage(MainPageViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
		InitializeSplitterInteraction();
		Loaded += OnPageLoaded;
		HandlerChanged += (_, _) => AttachKeyboardShortcutHandling();
	}

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	private void OnWorkbenchHostSizeChanged(object? sender, EventArgs e)
	{
		ViewModel.UpdateLayoutMode(WorkbenchHost.Width);
	}

	private void OnToggleLeftPaneClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleLeftPane();
	}

	private void OnToggleRightPaneClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleRightPane();
	}

	private async void OnSendClicked(object? sender, EventArgs e)
	{
		await ViewModel.SendAsync();
	}

	private void OnLeftSplitterPanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		if (!ViewModel.IsDesktopLayout)
		{
			return;
		}

		switch (e.StatusType)
		{
			case GestureStatus.Started:
				_leftPaneWidthOnDragStart = ViewModel.LeftPaneWidth.Value;
				SetSplitterActive(LeftSplitterLine, true);
				break;
			case GestureStatus.Running:
				ViewModel.ResizeLeftPane(_leftPaneWidthOnDragStart + e.TotalX, WorkbenchGrid.Width);
				break;
			case GestureStatus.Completed:
			case GestureStatus.Canceled:
				SetSplitterActive(LeftSplitterLine, false);
				break;
		}
	}

	private void OnRightSplitterPanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		if (!ViewModel.IsDesktopLayout)
		{
			return;
		}

		switch (e.StatusType)
		{
			case GestureStatus.Started:
				_rightPaneWidthOnDragStart = ViewModel.RightPaneWidth.Value;
				SetSplitterActive(RightSplitterLine, true);
				break;
			case GestureStatus.Running:
				ViewModel.ResizeRightPane(_rightPaneWidthOnDragStart - e.TotalX, WorkbenchGrid.Width);
				break;
			case GestureStatus.Completed:
			case GestureStatus.Canceled:
				SetSplitterActive(RightSplitterLine, false);
				break;
		}
	}

	private void OnOverlayBackdropTapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
	{
		ViewModel.DismissOverlays();
	}

	private void InitializeSplitterInteraction()
	{
		LeftSplitterLane.HandlerChanged += (_, _) => AttachSplitterPointerBehavior(LeftSplitterLane, LeftSplitterLine, isLeftSplitter: true);
		RightSplitterLane.HandlerChanged += (_, _) => AttachSplitterPointerBehavior(RightSplitterLane, RightSplitterLine, isLeftSplitter: false);
	}

	private async void OnPageLoaded(object? sender, EventArgs e)
	{
		if (_isInitialized)
		{
			return;
		}

		_isInitialized = true;
		await ViewModel.InitializeAsync();
	}

	private static void SetSplitterActive(BoxView splitterLine, bool isActive)
	{
		splitterLine.Opacity = isActive ? 1d : 0.72d;
	}

	private void AttachSplitterPointerBehavior(Grid splitterLane, BoxView splitterLine, bool isLeftSplitter)
	{
#if WINDOWS
		if (splitterLane.Handler?.PlatformView is not UIElement platformView)
		{
			return;
		}

		if (isLeftSplitter && ReferenceEquals(_leftSplitterNativeView, platformView))
		{
			return;
		}

		if (!isLeftSplitter && ReferenceEquals(_rightSplitterNativeView, platformView))
		{
			return;
		}

		if (isLeftSplitter)
		{
			_leftSplitterNativeView = platformView;
		}
		else
		{
			_rightSplitterNativeView = platformView;
		}

		SetResizeCursor(platformView);
		platformView.PointerEntered += (_, _) => SetSplitterActive(splitterLine, true);
		platformView.PointerExited += (_, _) => SetSplitterActive(splitterLine, false);
#endif
	}

	private void AttachKeyboardShortcutHandling()
	{
#if WINDOWS
		if (Handler?.PlatformView is not FrameworkElement platformView)
		{
			return;
		}

		if (ReferenceEquals(_pageNativeView, platformView))
		{
			return;
		}

		_pageNativeView = platformView;
		_pageNativeView.KeyDown += OnNativeKeyDown;
#endif
	}

#if WINDOWS
	private static void SetResizeCursor(UIElement platformView)
	{
		ProtectedCursorProperty?.SetValue(platformView, ResizeCursor);
	}

	private async void OnNativeKeyDown(object sender, KeyRoutedEventArgs e)
	{
		bool isControlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
		if (e.Key == VirtualKey.F5 || (e.Key == VirtualKey.Enter && isControlPressed))
		{
			e.Handled = true;
			await ViewModel.SendAsync();
		}
	}
#endif
}
