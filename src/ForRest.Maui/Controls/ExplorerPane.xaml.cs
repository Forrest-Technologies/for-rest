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

	private void OnWorkspaceTapped(object? sender, TappedEventArgs e)
	{
		if (e.Parameter is WorkspaceItemViewModel workspace)
		{
			ViewModel.SelectWorkspace(workspace);
		}
	}

	private void OnHistoryTapped(object? sender, TappedEventArgs e)
	{
		if (e.Parameter is HistoryEntryViewModel entry)
		{
			ViewModel.SelectHistoryEntry(entry);
		}
	}

	private void OnMoveRequestUpClicked(object? sender, EventArgs e)
	{
		ViewModel.MoveSelectedRequestUp();
	}

	private void OnMoveRequestDownClicked(object? sender, EventArgs e)
	{
		ViewModel.MoveSelectedRequestDown();
	}

	private void OnWorkspaceNameCompleted(object? sender, EventArgs e)
	{
		if (sender is Entry entry)
		{
			ViewModel.RenameSelectedWorkspace(entry.Text);
		}
	}

	private void OnWorkspaceNameUnfocused(object? sender, FocusEventArgs e)
	{
		if (sender is Entry entry)
		{
			ViewModel.RenameSelectedWorkspace(entry.Text);
		}
	}
}
