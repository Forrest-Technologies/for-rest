using ForRest.Maui.ViewModels;

namespace ForRest.Maui.Controls;

public partial class ExplorerPane : ContentView
{
	#region Constants

	private const string RenameAction = "Rename";
	private const string DeleteAction = "Delete";
	private const string OpenAction = "Open";
	private const string CancelAction = "Cancel";

	#endregion

	#region Constructors

	public ExplorerPane()
	{
		InitializeComponent();
	}

	#endregion

	#region Properties

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	#endregion

	#region Pane And Tab Handlers

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

	#endregion

	#region Workspace Switcher Handlers

	private void OnWorkspaceSwitcherTapped(object? sender, TappedEventArgs e)
	{
		ViewModel.ToggleWorkspaceSwitcher();
	}

	private void OnWorkspaceSwitcherBackdropTapped(object? sender, TappedEventArgs e)
	{
		ViewModel.CloseWorkspaceSwitcher();
	}

	private void OnWorkspaceTapped(object? sender, TappedEventArgs e)
	{
		if (e.Parameter is WorkspaceItemViewModel workspace)
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

	private async void OnWorkspaceActionsClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: WorkspaceItemViewModel workspace })
		{
			return;
		}

		ViewModel.SelectWorkspace(workspace);

		string? action = await ShowActionSheetAsync(workspace.Title, RenameAction, DeleteAction);
		switch (action)
		{
			case RenameAction:
				string? renamed = await PromptAsync("Rename Workspace", "Workspace name", workspace.Title);
				if (!string.IsNullOrWhiteSpace(renamed))
				{
					ViewModel.RenameSelectedWorkspace(renamed);
				}

				break;
			case DeleteAction:
				if (await ConfirmDeletionAsync(
					    "Delete Workspace",
					    $"Delete workspace '{workspace.Title}'? This cannot be undone.",
					    DeleteAction))
				{
					ViewModel.DeleteSelectedWorkspace();
				}

				break;
		}
	}

	#endregion

	#region Explorer Item Handlers

	private void OnItemTapped(object? sender, TappedEventArgs e)
	{
		if (e.Parameter is NavigationItemViewModel item)
		{
			ViewModel.SelectExplorerItem(item);
		}
	}

	private void OnAddRequestClicked(object? sender, EventArgs e)
	{
		ViewModel.AddRequest();
	}

	private async void OnItemActionsClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: NavigationItemViewModel item })
		{
			return;
		}

		ViewModel.SelectExplorerItem(item);

		string? action = await ShowActionSheetAsync(item.Title, OpenAction, RenameAction, DeleteAction);
		switch (action)
		{
			case OpenAction:
				ViewModel.SelectExplorerItem(item);
				break;
			case RenameAction:
				string? renamed = await PromptAsync("Rename", "Name", ViewModel.RequestName);
				if (!string.IsNullOrWhiteSpace(renamed))
				{
					ViewModel.RequestName = renamed.Trim();
				}

				break;
			case DeleteAction:
				if (await ConfirmDeletionAsync(
					    "Delete Request",
					    $"Delete '{item.Title}'? This cannot be undone.",
					    DeleteAction))
				{
					ViewModel.DeleteSelectedRequest();
				}

				break;
		}
	}

	#endregion

	#region History Handlers

	private void OnHistoryTapped(object? sender, TappedEventArgs e)
	{
		if (e.Parameter is HistoryEntryViewModel entry)
		{
			ViewModel.SelectHistoryEntry(entry);
		}
	}

	#endregion

	#region Dialog Helpers

	private async Task<bool> ConfirmDeletionAsync(string title, string message, string acceptText)
	{
		Page? page = FindParentPage();
		if (page is null)
		{
			return false;
		}

		return await page.DisplayAlert(title, message, acceptText, CancelAction);
	}

	private async Task<string?> ShowActionSheetAsync(string title, params string[] actions)
	{
		Page? page = FindParentPage();
		if (page is null)
		{
			return null;
		}

		return await page.DisplayActionSheet(title, CancelAction, null, actions);
	}

	private async Task<string?> PromptAsync(string title, string placeholder, string initialValue)
	{
		Page? page = FindParentPage();
		if (page is null)
		{
			return null;
		}

		return await page.DisplayPromptAsync(
			title,
			null,
			accept: "Save",
			cancel: CancelAction,
			placeholder: placeholder,
			initialValue: initialValue);
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

	#endregion
}
