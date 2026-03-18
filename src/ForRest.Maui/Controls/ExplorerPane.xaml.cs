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
		if (e.Parameter is ExplorerItemViewModel item)
		{
			ViewModel.SelectExplorerItem(item);
		}
	}
}
