using ForRest.Maui.ViewModels;

namespace ForRest.Maui.Controls;

public partial class WorkbenchCenterPane : ContentView
{
	public WorkbenchCenterPane()
	{
		InitializeComponent();
	}

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	private void OnTabClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: PaneTabViewModel tab })
		{
			ViewModel.SelectCenterTab(tab);
		}
	}

	private async void OnEditorSendRequested(object? sender, EventArgs e)
	{
		await ViewModel.SendAsync();
	}

	private async void OnSendClicked(object? sender, EventArgs e)
	{
		await ViewModel.SendAsync();
	}
}
