using ForRest.Maui.ViewModels;

namespace ForRest.Maui;

public partial class MainPage : ContentPage
{
	private double _leftPaneWidthOnDragStart;
	private double _rightPaneWidthOnDragStart;

	public MainPage(MainPageViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
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
				break;
			case GestureStatus.Running:
				ViewModel.ResizeLeftPane(_leftPaneWidthOnDragStart + e.TotalX, WorkbenchGrid.Width);
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
				break;
			case GestureStatus.Running:
				ViewModel.ResizeRightPane(_rightPaneWidthOnDragStart - e.TotalX, WorkbenchGrid.Width);
				break;
		}
	}

	private void OnOverlayBackdropTapped(object? sender, TappedEventArgs e)
	{
		ViewModel.DismissOverlays();
	}
}
