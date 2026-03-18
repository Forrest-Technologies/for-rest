using ForRest.Maui.ViewModels;

namespace ForRest.Maui.Controls;

public partial class InspectorPane : ContentView
{
	public InspectorPane()
	{
		InitializeComponent();
	}

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	private void OnHideClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleRightPane();
	}

	private void OnTabClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: PaneTabViewModel tab })
		{
			ViewModel.SelectRightPaneTab(tab);
		}
	}
}
