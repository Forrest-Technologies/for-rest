using ForRest.Maui.Services;
using ForRest.Maui.ViewModels;

namespace ForRest.Maui.Controls;

public partial class InspectorPane : ContentView
{
	public InspectorPane()
	{
		InitializeComponent();
		Loaded += (_, _) => EnsureResponseBodyViewer();
	}

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	protected override void OnBindingContextChanged()
	{
		base.OnBindingContextChanged();
		EnsureResponseBodyViewer();
	}

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

	private async void OnExportStashClicked(object? sender, EventArgs e)
	{
		await ViewModel.ExportStashCsvAsync();
	}

	private async void OnResponseVarCopyRequested(object? sender, MonacoResponseVarRequestEventArgs e)
	{
		await ViewModel.CopyResponseVariableAsync(e.LineNumber, e.Column);
	}

	private void EnsureResponseBodyViewer()
	{
		if (ResponseBodyViewerHost.Content is not null)
		{
			return;
		}

		ResponseBodyViewerHost.Content = PlatformExperience.UseWebCodeEditors() && !AppLaunchGuard.IsSafeModeEnabled
			? BuildMonacoResponseViewer()
			: BuildNativeResponseViewer();
	}

	private View BuildMonacoResponseViewer()
	{
		MonacoEditorSurface viewer = new()
		{
			Language = "json",
			IsReadOnly = true,
			EnableResponseActions = true
		};
		viewer.SetBinding(MonacoEditorSurface.ThemeKeyProperty, nameof(MainPageViewModel.EditorThemeKey));
		viewer.SetBinding(MonacoEditorSurface.TextProperty, nameof(MainPageViewModel.ResponseBodyText));
		viewer.ResponseVarCopyRequested += OnResponseVarCopyRequested;
		return viewer;
	}

	private View BuildNativeResponseViewer()
	{
		EditorSurface viewer = new()
		{
			Language = "json",
			IsReadOnly = true,
			ShowHeader = false,
			ShowFooter = false
		};
		viewer.SetBinding(EditorSurface.TextProperty, nameof(MainPageViewModel.ResponseBodyText));
		return viewer;
	}
}
