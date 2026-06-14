using ForRest.Maui.ViewModels;
using ForRest.Maui.Services;
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
	private bool _isPreparingForShutdown;
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

	public async Task PrepareForShutdownAsync()
	{
		if (_isPreparingForShutdown)
		{
			return;
		}

		_isPreparingForShutdown = true;

		try
		{
			await CenterPane.PrepareForShutdownAsync();
			await ViewModel.PrepareForShutdownAsync();
		}
		finally
		{
#if WINDOWS
			if (_pageNativeView is not null)
			{
				_pageNativeView.KeyDown -= OnNativeKeyDown;
				_pageNativeView = null;
			}
#endif
		}
	}

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

	private void OnToggleCenterPaneClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleCenterPane();
	}

	private void OnDismissStatusBannerClicked(object? sender, EventArgs e)
	{
		ViewModel.DismissStatusBanner();
	}

	private async void OnSendClicked(object? sender, EventArgs e)
	{
		await CenterPane.FlushActiveEditorAsync();
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
		Loaded -= OnPageLoaded;
		try
		{
			AppLaunchGuard.RecordMessage("MainPage startup", "MainPage loaded; initialization starting.");
			await ViewModel.InitializeAsync();
			AppLaunchGuard.RecordMessage("MainPage startup", "MainPage initialization completed successfully.");
			AppLaunchGuard.MarkLaunchCompleted();
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("MainPage initialization failed.", exception);
			Content = new ScrollView
			{
				Content = new VerticalStackLayout
				{
					Padding = new Microsoft.Maui.Thickness(24),
					Spacing = 12,
					Children =
					{
						new Label
						{
							Text = "ForRest failed during startup.",
							FontAttributes = FontAttributes.Bold,
							FontSize = 20
						},
						new Label
						{
							Text = $"Diagnostics were written to:{Environment.NewLine}{AppLaunchGuard.StartupLogPath}"
						},
						new Label
						{
							Text = exception.ToString()
						}
					}
				}
			};
		}
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
		// async void: an exception escaping past the first await would take down the process,
		// not just the shortcut, so the whole handler stays guarded.
		try
		{
			bool isControlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
			bool isShiftPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
			if (e.Key == VirtualKey.F5 || (e.Key == VirtualKey.Enter && isControlPressed))
			{
				e.Handled = true;
				await CenterPane.FlushActiveEditorAsync();
				await ViewModel.SendAsync();
				return;
			}

			if (isControlPressed && e.Key == VirtualKey.Z && !isShiftPressed)
			{
				e.Handled = true;
				await CenterPane.UndoActiveDocumentAsync();
				return;
			}

			if (isControlPressed && (e.Key == VirtualKey.Y || (isShiftPressed && e.Key == VirtualKey.Z)))
			{
				e.Handled = true;
				await CenterPane.RedoActiveDocumentAsync();
			}
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Keyboard shortcut handling failed.", exception);
		}
	}
#endif
}
