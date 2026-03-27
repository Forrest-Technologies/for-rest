using System.ComponentModel;
using ForRest.Maui.Services;
using ForRest.Maui.ViewModels;

namespace ForRest.Maui.Controls;

public partial class InspectorPane : ContentView
{
	private INotifyPropertyChanged? _viewModelNotifier;

	public InspectorPane()
	{
		InitializeComponent();
		Loaded += (_, _) => EnsureResponseBodyViewer();
	}

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	protected override void OnBindingContextChanged()
	{
		if (_viewModelNotifier is not null)
		{
			_viewModelNotifier.PropertyChanged -= OnViewModelPropertyChanged;
			_viewModelNotifier = null;
		}

		base.OnBindingContextChanged();

		if (BindingContext is INotifyPropertyChanged notifier)
		{
			_viewModelNotifier = notifier;
			_viewModelNotifier.PropertyChanged += OnViewModelPropertyChanged;
		}

		EnsureResponseBodyViewer();
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(e.PropertyName) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.IsInspectorResponseVisible), StringComparison.Ordinal))
		{
			EnsureResponseBodyViewer();
		}
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
			EnsureResponseBodyViewer();
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
		if (ResponseBodyViewerHost.Content is not null || !IsVisible || !ViewModel.IsInspectorResponseVisible)
		{
			return;
		}

		ResponseBodyViewerHost.Content = PlatformExperience.UseWebCodeEditors() && !AppLaunchGuard.IsSafeModeEnabled
			? BuildMonacoResponseViewer()
			: BuildNativeResponseViewer();
	}

	protected override void OnPropertyChanged(string? propertyName = null)
	{
		base.OnPropertyChanged(propertyName);

		if (string.Equals(propertyName, nameof(IsVisible), StringComparison.Ordinal) && IsVisible)
		{
			EnsureResponseBodyViewer();
		}
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
