using ForRest.Maui.ViewModels;

namespace ForRest.Maui.Controls;

public partial class ExplorerPane : ContentView
{
	public ExplorerPane()
	{
		InitializeComponent();
	}

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	private void OnHideClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleLeftPane();
	}

	private void OnTabClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: PaneTabViewModel tab })
		{
			ViewModel.SelectLeftPaneTab(tab);
		}
	}

	private void OnItemTapped(object? sender, TappedEventArgs e)
	{
		if (e.Parameter is NavigationItemViewModel item)
		{
			ViewModel.SelectExplorerItem(item);
		}
	}

	private void OnWorkspaceClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: WorkspaceItemViewModel workspace })
		{
			ViewModel.SelectWorkspace(workspace);
		}
	}

	private void OnAddWorkspaceClicked(object? sender, EventArgs e)
	{
		ViewModel.AddWorkspace();
	}

	private void OnMoveWorkspaceLeftClicked(object? sender, EventArgs e)
	{
		ViewModel.MoveSelectedWorkspaceLeft();
	}

	private void OnMoveWorkspaceRightClicked(object? sender, EventArgs e)
	{
		ViewModel.MoveSelectedWorkspaceRight();
	}

	private void OnAddRequestClicked(object? sender, EventArgs e)
	{
		ViewModel.AddRequest();
	}

	private void OnMoveRequestUpClicked(object? sender, EventArgs e)
	{
		ViewModel.MoveSelectedRequestUp();
	}

	private void OnMoveRequestDownClicked(object? sender, EventArgs e)
	{
		ViewModel.MoveSelectedRequestDown();
	}
}
