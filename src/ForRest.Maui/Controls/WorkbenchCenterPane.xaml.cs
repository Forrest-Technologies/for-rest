using ForRest.Maui.ViewModels;

namespace ForRest.Maui.Controls;

public partial class WorkbenchCenterPane : ContentView
{
	public WorkbenchCenterPane()
	{
		InitializeComponent();
	}

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	private void OnDocumentClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: RequestDocumentViewModel document })
		{
			ViewModel.SelectDocument(document);
		}
	}

	private void OnTabClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: PaneTabViewModel tab })
		{
			ViewModel.SelectCenterTab(tab);
		}
	}
}
