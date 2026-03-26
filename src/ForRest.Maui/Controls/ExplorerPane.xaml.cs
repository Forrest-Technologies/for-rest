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

	private async void OnDeleteWorkspaceClicked(object? sender, EventArgs e)
	{
		if (!await ConfirmDeletionAsync(
			    "Delete Workspace",
			    $"Delete workspace '{ViewModel.SelectedWorkspace}'? This cannot be undone.",
			    "Delete"))
		{
			return;
		}

		ViewModel.DeleteSelectedWorkspace();
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

	private async void OnDeleteRequestClicked(object? sender, EventArgs e)
	{
		if (!await ConfirmDeletionAsync(
			    "Delete Request",
			    $"Delete request '{ViewModel.RequestName}'? This cannot be undone.",
			    "Delete"))
		{
			return;
		}

		ViewModel.DeleteSelectedRequest();
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

	private async Task<bool> ConfirmDeletionAsync(string title, string message, string acceptText)
	{
		Page? page = FindParentPage();
		if (page is null)
		{
			return false;
		}

		return await page.DisplayAlert(title, message, acceptText, "Cancel");
	}

	private Page? FindParentPage()
	{
		Element? current = this;
		while (current is not null)
		{
			if (current is Page page)
			{
				return page;
			}

			current = current.Parent;
		}

		return null;
	}
}
