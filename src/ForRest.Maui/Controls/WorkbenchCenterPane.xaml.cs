using System.ComponentModel;
using System.Linq;
using ForRest.Maui.ViewModels;
using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using Microsoft.Maui.Controls.Shapes;

namespace ForRest.Maui.Controls;

public partial class WorkbenchCenterPane : ContentView
{
	private INotifyPropertyChanged? _viewModelNotifier;

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

		EnsureEditorSurface();
		EnsureLanguageHelpExampleSurfaces();
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(e.PropertyName) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.ShowInlineLanguageHelpDrawer), StringComparison.Ordinal) ||
		    string.Equals(e.PropertyName, nameof(MainPageViewModel.ShowCompactLanguageHelpDrawer), StringComparison.Ordinal))
		{
			EnsureLanguageHelpExampleSurfaces();
		}
	}

	private async void OnEditorSendRequested(object? sender, EventArgs e)
	{
		await SendActiveDocumentAsync();
	}

	private async void OnEditorUndoRequested(object? sender, EventArgs e)
	{
		await UndoActiveDocumentAsync();
	}

	private async void OnEditorRedoRequested(object? sender, EventArgs e)
	{
		await RedoActiveDocumentAsync();
	}

	private void OnEditorCursorPositionChanged(object? sender, EditorCursorPositionChangedEventArgs e)
	{
		ViewModel.UpdateActiveEditorCursor(e.LineNumber, e.Column);
	}

	private async void OnSendClicked(object? sender, EventArgs e)
	{
		await SendActiveDocumentAsync();
	}

	private async void OnCopyClicked(object? sender, EventArgs e)
	{
		await ViewModel.CopyActiveEditorAsync();
	}

	private async void OnPasteClicked(object? sender, EventArgs e)
	{
		await PasteActiveDocumentAsync();
	}

	private async void OnUndoClicked(object? sender, EventArgs e)
	{
		await UndoActiveDocumentAsync();
	}

	private async void OnRedoClicked(object? sender, EventArgs e)
	{
		await RedoActiveDocumentAsync();
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
		if (BindingContext is not MainPageViewModel viewModel)
		{
			return;
		}

		if (viewModel.ShowInlineLanguageHelpDrawer && InlineLanguageHelpExampleHost.Content is null)
		{
			InlineLanguageHelpExampleHost.Content = BuildLanguageHelpExampleSurface();
		}

		if (viewModel.ShowCompactLanguageHelpDrawer && CompactLanguageHelpExampleHost.Content is null)
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

		if (PlatformExperience.UseSoraEditor())
		{
			return BuildSoraEditor();
		}

		return PlatformExperience.UseWebCodeEditors()
			? BuildMonacoEditor()
			: BuildNativeEditor();
	}

	public async Task FlushActiveEditorAsync()
	{
		if (EditorHost.Content is ICodeEditorSurface editor)
		{
			await editor.FlushTextSyncAsync();
		}
	}

	public async Task PrepareForShutdownAsync()
	{
		await FlushActiveEditorAsync();

		if (EditorHost.Content is ICodeEditorSurface editor)
		{
			editor.SendRequested -= OnEditorSendRequested;
			editor.UndoRequested -= OnEditorUndoRequested;
			editor.RedoRequested -= OnEditorRedoRequested;
			editor.CursorPositionChanged -= OnEditorCursorPositionChanged;
			editor.EditorFocusChanged -= OnMonacoEditorFocusChanged;
		}

		if (_viewModelNotifier is not null)
		{
			_viewModelNotifier.PropertyChanged -= OnViewModelPropertyChanged;
			_viewModelNotifier = null;
		}

		EditorHost.Content = null;
		InlineLanguageHelpExampleHost.Content = null;
		CompactLanguageHelpExampleHost.Content = null;
	}

	private async Task SendActiveDocumentAsync()
	{
		await FlushActiveEditorAsync();
		await ViewModel.SendAsync();
		await ApplyPendingCursorRequestAsync();
	}

	public async Task UndoActiveDocumentAsync()
	{
		await FlushActiveEditorAsync();
		ViewModel.Undo();
	}

	public async Task RedoActiveDocumentAsync()
	{
		await FlushActiveEditorAsync();
		ViewModel.Redo();
	}

	public async Task PasteActiveDocumentAsync()
	{
		switch (EditorHost.Content)
		{
			case ICodeEditorSurface editor:
				await editor.PasteFromClipboardAsync();
				break;
			case EditorSurface editorSurface:
				await editorSurface.PasteFromClipboardAsync();
				break;
		}
	}

	private async Task ApplyPendingCursorRequestAsync()
	{
		if (EditorHost.Content is not ICodeEditorSurface editor)
		{
			return;
		}

		if (!ViewModel.TryConsumePendingEditorCursorRequest(out int lineNumber, out int column))
		{
			return;
		}

		await editor.MoveCursorToAsync(lineNumber, column);
	}

	private View BuildMonacoEditor()
	{
		MonacoEditorSurface editor = new();
		editor.SetBinding(MonacoEditorSurface.LanguageProperty, nameof(MainPageViewModel.ActiveEditorLanguage));
		editor.SetBinding(MonacoEditorSurface.DiagnosticsJsonProperty, nameof(MainPageViewModel.ActiveEditorDiagnosticsJson));
		editor.SetBinding(MonacoEditorSurface.EditableRangesJsonProperty, nameof(MainPageViewModel.ActiveEditorEditableRangesJson));
		editor.SetBinding(MonacoEditorSurface.EditorFontSizeProperty, nameof(MainPageViewModel.ActiveEditorFontSize));
		editor.SetBinding(MonacoEditorSurface.ThemeKeyProperty, nameof(MainPageViewModel.EditorThemeKey));
		// Monaco stays bound to the raw ActiveEditorText so the model
		// always holds the real source. Secret values are hidden via
		// JavaScript decorations the Monaco host applies/removes on
		// focus change — that avoids the cursor jumping when a user
		// taps into a previously masked editor.
		editor.SetBinding(MonacoEditorSurface.TextProperty, nameof(MainPageViewModel.ActiveEditorText), mode: BindingMode.TwoWay);
		editor.SetBinding(MonacoEditorSurface.LanguageHelpJsonProperty, nameof(MainPageViewModel.LanguageHelpCatalogJson));
		editor.SetBinding(MonacoEditorSurface.RequestedCursorLineNumberProperty, nameof(MainPageViewModel.ActiveEditorRequestedCursorLineNumber));
		editor.SetBinding(MonacoEditorSurface.RequestedCursorColumnProperty, nameof(MainPageViewModel.ActiveEditorRequestedCursorColumn));
		editor.SetBinding(MonacoEditorSurface.RequestedCursorVersionProperty, nameof(MainPageViewModel.ActiveEditorRequestedCursorVersion));
		editor.SendRequested += OnEditorSendRequested;
		editor.UndoRequested += OnEditorUndoRequested;
		editor.RedoRequested += OnEditorRedoRequested;
		editor.CursorPositionChanged += OnEditorCursorPositionChanged;
		editor.EditorFocusChanged += OnMonacoEditorFocusChanged;
		return editor;
	}

	private View BuildSoraEditor()
	{
		SoraEditorSurface editor = new();
		editor.SetBinding(SoraEditorSurface.LanguageProperty, nameof(MainPageViewModel.ActiveEditorLanguage));
		editor.SetBinding(SoraEditorSurface.DiagnosticsJsonProperty, nameof(MainPageViewModel.ActiveEditorDiagnosticsJson));
		editor.SetBinding(SoraEditorSurface.EditableRangesJsonProperty, nameof(MainPageViewModel.ActiveEditorEditableRangesJson));
		editor.SetBinding(SoraEditorSurface.EditorFontSizeProperty, nameof(MainPageViewModel.ActiveEditorFontSize));
		editor.SetBinding(SoraEditorSurface.ThemeKeyProperty, nameof(MainPageViewModel.EditorThemeKey));
		// Like Monaco, the native Sora editor stays bound to the raw ActiveEditorText; secret
		// masking is driven by the focus events relayed through the view-model.
		editor.SetBinding(SoraEditorSurface.TextProperty, nameof(MainPageViewModel.ActiveEditorText), mode: BindingMode.TwoWay);
		editor.SetBinding(SoraEditorSurface.LanguageHelpJsonProperty, nameof(MainPageViewModel.LanguageHelpCatalogJson));
		editor.SetBinding(SoraEditorSurface.RequestedCursorLineNumberProperty, nameof(MainPageViewModel.ActiveEditorRequestedCursorLineNumber));
		editor.SetBinding(SoraEditorSurface.RequestedCursorColumnProperty, nameof(MainPageViewModel.ActiveEditorRequestedCursorColumn));
		editor.SetBinding(SoraEditorSurface.RequestedCursorVersionProperty, nameof(MainPageViewModel.ActiveEditorRequestedCursorVersion));
		editor.SendRequested += OnEditorSendRequested;
		editor.UndoRequested += OnEditorUndoRequested;
		editor.RedoRequested += OnEditorRedoRequested;
		editor.CursorPositionChanged += OnEditorCursorPositionChanged;
		editor.EditorFocusChanged += OnMonacoEditorFocusChanged;
		return editor;
	}

	private void OnMonacoEditorFocusChanged(object? sender, EditorFocusChangedEventArgs e)
	{
		if (BindingContext is MainPageViewModel viewModel)
		{
			viewModel.SetActiveEditorFocus(e.IsFocused);
		}
	}

	private View BuildLanguageHelpExampleSurface()
	{
		if (AppLaunchGuard.IsSafeModeEnabled)
		{
			return BuildLanguageHelpFallbackEditor();
		}

		if (PlatformExperience.UseSoraEditor())
		{
			SoraEditorSurface soraEditor = new()
			{
				IsReadOnly = true,
				Language = "forrest",
			};
			soraEditor.SetBinding(SoraEditorSurface.TextProperty, nameof(MainPageViewModel.SelectedLanguageHelpExample));
			soraEditor.SetBinding(SoraEditorSurface.ThemeKeyProperty, nameof(MainPageViewModel.EditorThemeKey));
			return soraEditor;
		}

		if (!PlatformExperience.UseWebCodeEditors())
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
		editor.SetBinding(EditorSurface.EditorFontSizeProperty, nameof(MainPageViewModel.ActiveEditorFontSize));
		editor.SetBinding(EditorSurface.LanguageProperty, nameof(MainPageViewModel.ActiveEditorLanguage));
		// Bind to the presentation text so secret values stay masked
		// while the user is not actively editing the request script.
		editor.SetBinding(EditorSurface.TextProperty, nameof(MainPageViewModel.ActiveEditorPresentationText), mode: BindingMode.TwoWay);
		editor.InnerEditorFocused += OnNativeEditorFocused;
		editor.InnerEditorUnfocused += OnNativeEditorUnfocused;
		return editor;
	}

	private void OnNativeEditorFocused(object? sender, FocusEventArgs e)
	{
		if (BindingContext is MainPageViewModel viewModel)
		{
			viewModel.SetActiveEditorFocus(true);
		}
	}

	private void OnNativeEditorUnfocused(object? sender, FocusEventArgs e)
	{
		if (BindingContext is MainPageViewModel viewModel)
		{
			viewModel.SetActiveEditorFocus(false);
		}
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
		};
		Grid bannerContent = new()
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto)
			},
			ColumnSpacing = 8
		};
		Label bannerLabel = new()
		{
			Text = recoveryMessage,
			LineBreakMode = LineBreakMode.WordWrap,
			FontSize = 12,
			TextColor = Color.FromArgb("#6A5034")
		};
		Button dismissButton = new()
		{
			Text = "✕",
			Padding = new Thickness(8, 2),
			VerticalOptions = LayoutOptions.Start,
			HorizontalOptions = LayoutOptions.End
		};
		dismissButton.Clicked += (_, _) => banner.IsVisible = false;
		bannerContent.Children.Add(bannerLabel);
		bannerContent.Children.Add(dismissButton);
		Grid.SetColumn(dismissButton, 1);
		banner.Content = bannerContent;

		Editor editor = new()
		{
			AutoSize = EditorAutoSizeOption.Disabled,
			FontFamily = "OpenSansRegular",
			Margin = new Thickness(8, 8, 8, 8)
		};
		editor.SetDynamicResource(InputView.TextColorProperty, ThemeResourceKeys.TextPrimaryColor);
		editor.SetDynamicResource(VisualElement.BackgroundColorProperty, ThemeResourceKeys.EditorBackgroundColor);
		editor.SetBinding(Editor.FontSizeProperty, nameof(MainPageViewModel.ActiveEditorFontSize));
		editor.SetBinding(Editor.TextProperty, nameof(MainPageViewModel.ActiveEditorPresentationText), mode: BindingMode.TwoWay);
		editor.Focused += OnNativeEditorFocused;
		editor.Unfocused += OnNativeEditorUnfocused;

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
			Margin = new Thickness(8)
		};
		editor.SetDynamicResource(InputView.TextColorProperty, ThemeResourceKeys.TextPrimaryColor);
		editor.SetDynamicResource(VisualElement.BackgroundColorProperty, ThemeResourceKeys.EditorBackgroundColor);
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
