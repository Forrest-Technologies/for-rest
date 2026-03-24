using ForRest.Maui.ViewModels;

namespace ForRest.Maui.Controls;

public partial class InspectorPane : ContentView
{
	public InspectorPane()
	{
		InitializeComponent();
		ResponseBodyViewer.ResponseVarCopyRequested += OnResponseVarCopyRequested;
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

	private void OnTogglePrettyPrintClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleResponsePrettyPrint();
	}

	private async void OnCopyResponseClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyResponseBodyAsync();
	}

	private async void OnCopyRawResponseClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyRawResponseAsync();
	}

	private async void OnResponseVarCopyRequested(object? sender, MonacoResponseVarRequestEventArgs e)
	{
		await ViewModel.CopyResponseVariableAsync(e.LineNumber, e.Column);
	}
}
