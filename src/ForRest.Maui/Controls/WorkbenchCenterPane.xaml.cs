using System.Linq;
using ForRest.Maui.ViewModels;
using ForRest.Maui.Services;
using Microsoft.Maui.Controls.Shapes;

namespace ForRest.Maui.Controls;

public partial class WorkbenchCenterPane : ContentView
{
	public WorkbenchCenterPane()
	{
		InitializeComponent();
		Loaded += (_, _) =>
		{
			EnsureEditorSurface();
			EnsureLanguageHelpExampleSurfaces();
		};
	}

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	protected override void OnBindingContextChanged()
	{
		base.OnBindingContextChanged();
		EnsureEditorSurface();
		EnsureLanguageHelpExampleSurfaces();
	}

	private async void OnEditorSendRequested(object? sender, EventArgs e)
	{
		await ViewModel.SendAsync();
	}

	private async void OnSendClicked(object? sender, EventArgs e)
	{
		await ViewModel.SendAsync();
	}

	private async void OnCopyClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyActiveEditorAsync();
	}

	private void OnToggleLanguageHelpClicked(object? sender, EventArgs e)
	{
		ViewModel.ToggleLanguageHelp();
	}

	private async void OnCopyLanguageHelpExampleClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopySelectedLanguageHelpExampleAsync();

		if (sender is Button button && ViewModel.CanCopyLanguageHelpExample)
		{
			await ShowCopiedStateAsync(button);
		}
	}

	private void OnLanguageHelpSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		ViewModel.SelectLanguageHelpEntry(e.CurrentSelection.OfType<LanguageHelpEntryViewModel>().FirstOrDefault());
	}

	private void EnsureEditorSurface()
	{
		if (EditorHost.Content is not null)
		{
			return;
		}

		try
		{
			EditorHost.Content = BuildPreferredEditor();
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Failed to construct the workbench editor surface.", exception);
			EditorHost.Content = BuildFallbackEditor();
		}
	}

	private void EnsureLanguageHelpExampleSurfaces()
	{
		if (InlineLanguageHelpExampleHost.Content is null)
		{
			InlineLanguageHelpExampleHost.Content = BuildLanguageHelpExampleSurface();
		}

		if (CompactLanguageHelpExampleHost.Content is null)
		{
			CompactLanguageHelpExampleHost.Content = BuildLanguageHelpExampleSurface();
		}
	}

	private View BuildPreferredEditor()
	{
		if (AppLaunchGuard.IsSafeModeEnabled)
		{
			return BuildFallbackEditor();
		}

		return PlatformExperience.UseWebCodeEditors()
			? BuildMonacoEditor()
			: BuildNativeEditor();
	}

	private View BuildMonacoEditor()
	{
		MonacoEditorSurface editor = new();
		editor.SetBinding(MonacoEditorSurface.LanguageProperty, nameof(MainPageViewModel.ActiveEditorLanguage));
		editor.SetBinding(MonacoEditorSurface.DiagnosticsJsonProperty, nameof(MainPageViewModel.ActiveEditorDiagnosticsJson));
		editor.SetBinding(MonacoEditorSurface.EditableRangesJsonProperty, nameof(MainPageViewModel.ActiveEditorEditableRangesJson));
		editor.SetBinding(MonacoEditorSurface.ThemeKeyProperty, nameof(MainPageViewModel.EditorThemeKey));
		editor.SetBinding(MonacoEditorSurface.TextProperty, nameof(MainPageViewModel.ActiveEditorText), mode: BindingMode.TwoWay);
		editor.SetBinding(MonacoEditorSurface.LanguageHelpJsonProperty, nameof(MainPageViewModel.LanguageHelpCatalogJson));
		editor.SendRequested += OnEditorSendRequested;
		return editor;
	}

	private View BuildLanguageHelpExampleSurface()
	{
		if (AppLaunchGuard.IsSafeModeEnabled || !PlatformExperience.UseWebCodeEditors())
		{
			return BuildLanguageHelpFallbackEditor();
		}

		MonacoEditorSurface editor = new()
		{
			IsReadOnly = true,
		};
		editor.SetBinding(MonacoEditorSurface.TextProperty, nameof(MainPageViewModel.SelectedLanguageHelpExample));
		editor.SetBinding(MonacoEditorSurface.ThemeKeyProperty, nameof(MainPageViewModel.EditorThemeKey));
		editor.Language = "forrest";
		return editor;
	}

	private View BuildNativeEditor()
	{
		EditorSurface editor = new()
		{
			ShowHeader = false,
			ShowFooter = false
		};
		editor.SetBinding(EditorSurface.LanguageProperty, nameof(MainPageViewModel.ActiveEditorLanguage));
		editor.SetBinding(EditorSurface.TextProperty, nameof(MainPageViewModel.ActiveEditorText), mode: BindingMode.TwoWay);
		return editor;
	}

	private View BuildFallbackEditor()
	{
		string recoveryMessage = AppLaunchGuard.IsSafeModeEnabled
			? $"{AppLaunchGuard.SafeModeReason}{Environment.NewLine}Diagnostics: {AppLaunchGuard.StartupLogPath}"
			: $"ForRest switched to the fallback editor because the Monaco surface failed to initialize.{Environment.NewLine}Diagnostics: {AppLaunchGuard.StartupLogPath}";

		Border banner = new()
		{
			Margin = new Thickness(10, 10, 10, 0),
			Padding = new Thickness(10, 8),
			BackgroundColor = Color.FromArgb("#FFF6E6"),
			Stroke = Color.FromArgb("#D8B46E"),
			StrokeThickness = 1,
			StrokeShape = new RoundRectangle
			{
				CornerRadius = new CornerRadius(8)
			},
			Content = new Label
			{
				Text = recoveryMessage,
				LineBreakMode = LineBreakMode.WordWrap,
				FontSize = 12,
				TextColor = Color.FromArgb("#6A5034")
			}
		};

		Editor editor = new()
		{
			AutoSize = EditorAutoSizeOption.Disabled,
			FontFamily = "OpenSansRegular",
			TextColor = Color.FromArgb("#16202A"),
			BackgroundColor = Colors.Transparent,
			Margin = new Thickness(8, 8, 8, 8)
		};
		editor.SetBinding(Editor.TextProperty, nameof(MainPageViewModel.ActiveEditorText), mode: BindingMode.TwoWay);

		Grid grid = new()
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star)
			},
			Children =
			{
				banner,
				editor
			}
		};
		Grid.SetRow(banner, 0);
		Grid.SetRow(editor, 1);
		return grid;
	}

	private View BuildLanguageHelpFallbackEditor()
	{
		Editor editor = new()
		{
			AutoSize = EditorAutoSizeOption.Disabled,
			IsReadOnly = true,
			FontFamily = "OpenSansRegular",
			FontSize = 12,
			TextColor = Color.FromArgb("#16202A"),
			BackgroundColor = Colors.Transparent,
			Margin = new Thickness(8)
		};
		editor.SetBinding(Editor.TextProperty, nameof(MainPageViewModel.SelectedLanguageHelpExample));
		return editor;
	}

	private static async Task ShowCopiedStateAsync(Button button)
	{
		string originalText = button.Text;
		button.Text = "Copied";

		try
		{
			await Task.Delay(1200);
		}
		finally
		{
			button.Text = originalText;
		}
	}
}
