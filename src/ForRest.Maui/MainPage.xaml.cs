using ForRest.Maui.ViewModels;
#if WINDOWS
using System.Reflection;
using Microsoft.UI.Xaml;
#endif

namespace ForRest.Maui;

public partial class MainPage : ContentPage
{
	private double _leftPaneWidthOnDragStart;
	private double _rightPaneWidthOnDragStart;
#if WINDOWS
	private UIElement? _leftSplitterNativeView;
	private UIElement? _rightSplitterNativeView;
	private static readonly PropertyInfo? ProtectedCursorProperty = typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly Microsoft.UI.Input.InputSystemCursor ResizeCursor =
		Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
#endif

	public MainPage(MainPageViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
		InitializeSplitterInteraction();
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

#if WINDOWS
	private static void SetResizeCursor(UIElement platformView)
	{
		ProtectedCursorProperty?.SetValue(platformView, ResizeCursor);
	}
#endif
}
