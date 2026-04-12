using ForRest.Maui.Services;
using ForRest.Maui.ViewModels;

namespace ForRest.Maui;

public partial class AndroidMainPage : ContentPage
{
	private bool _isInitialized;
	private AndroidOutputView _outputView = AndroidOutputView.Response;

	public AndroidMainPage(MainPageViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
		Loaded += OnPageLoaded;
		SizeChanged += OnPageSizeChanged;
		UpdateOutputView(AndroidOutputView.Response);
	}

	private MainPageViewModel? ViewModel => BindingContext as MainPageViewModel;

	public async Task PrepareForShutdownAsync()
	{
		if (ViewModel is null)
		{
			return;
		}

		await ViewModel.PrepareForShutdownAsync();
	}

	private async void OnPageLoaded(object? sender, EventArgs e)
	{
		if (_isInitialized)
		{
			return;
		}

		_isInitialized = true;
		try
		{
			if (ViewModel is null)
			{
				throw new InvalidOperationException("AndroidMainPage could not resolve MainPageViewModel.");
			}

			AppLaunchGuard.RecordMessage("AndroidMainPage startup", "AndroidMainPage loaded; initialization starting.");
			ViewModel.UpdateLayoutMode(Width);
			await ViewModel.InitializeAsync();
			AppLaunchGuard.RecordMessage("AndroidMainPage startup", "AndroidMainPage initialization completed successfully.");
			AppLaunchGuard.MarkLaunchCompleted();
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("AndroidMainPage initialization failed.", exception);
			Content = BuildFallbackContent(exception);
		}
	}

	private void OnPageSizeChanged(object? sender, EventArgs e)
	{
		if (ViewModel is null)
		{
			return;
		}

		ViewModel.UpdateLayoutMode(Width);
	}

	private async void OnSendClicked(object? sender, EventArgs e)
	{
		if (ViewModel is null)
		{
			return;
		}

		await ExecuteWithLaunchGuard(ViewModel.SendAsync, "Android send failed.");
	}

	private void OnDismissStatusBannerClicked(object? sender, EventArgs e)
	{
		ViewModel?.DismissStatusBanner();
	}

	private async void OnCopyRequestClicked(object? sender, EventArgs e)
	{
		if (ViewModel is null)
		{
			return;
		}

		await ExecuteWithLaunchGuard(ViewModel.CopyActiveEditorAsync, "Android request copy failed.");
	}

	private void OnShowResponseClicked(object? sender, EventArgs e)
	{
		UpdateOutputView(AndroidOutputView.Response);
	}

	private void OnShowRawClicked(object? sender, EventArgs e)
	{
		UpdateOutputView(AndroidOutputView.Raw);
	}

	private void OnShowDebugClicked(object? sender, EventArgs e)
	{
		UpdateOutputView(AndroidOutputView.Debug);
	}

	private void OnTogglePrettyPrintClicked(object? sender, EventArgs e)
	{
		if (ViewModel is null)
		{
			return;
		}

		try
		{
			ViewModel.ToggleResponsePrettyPrint();
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Android pretty-print toggle failed.", exception);
		}
	}

	private async void OnCopyOutputClicked(object? sender, EventArgs e)
	{
		if (ViewModel is null)
		{
			return;
		}

		try
		{
			switch (_outputView)
			{
				case AndroidOutputView.Response:
					await ViewModel.CopyResponseBodyAsync();
					break;
				case AndroidOutputView.Raw:
					await ViewModel.CopyRawResponseAsync();
					break;
				default:
					await ViewModel.CopyDebugOutputAsync();
					break;
			}
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Android output copy failed.", exception);
		}
	}

	private async void OnCopyDebugSummaryClicked(object? sender, EventArgs e)
	{
		if (ViewModel is null)
		{
			return;
		}

		try
		{
			await ViewModel.CopyDebugSummaryAsync();
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Android debug summary copy failed.", exception);
		}
	}

	private void OnRequestEditorFocused(object? sender, FocusEventArgs e)
	{
		// Reveal real secret values for the user the moment they tap
		// into the editor — they're now actively editing and need to
		// see what they're working with.
		ViewModel?.SetActiveEditorFocus(true);
	}

	private void OnRequestEditorUnfocused(object? sender, FocusEventArgs e)
	{
		// Re-mask secrets as soon as focus leaves so a phone left on a
		// desk doesn't expose credentials over someone's shoulder.
		ViewModel?.SetActiveEditorFocus(false);
	}

	private async Task ExecuteWithLaunchGuard(Func<Task> action, string context)
	{
		try
		{
			await action();
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException(context, exception);
		}
	}

	private void UpdateOutputView(AndroidOutputView outputView)
	{
		_outputView = outputView;
		ResponseOutputEditor.RemoveBinding(Editor.TextProperty);
		ResponseOutputEditor.SetBinding(
			Editor.TextProperty,
			outputView switch
			{
				AndroidOutputView.Raw => nameof(MainPageViewModel.ResponseRawText),
				AndroidOutputView.Debug => nameof(MainPageViewModel.DebugOutputText),
				_ => nameof(MainPageViewModel.ResponseBodyText)
			});
		OutputTitleLabel.Text = outputView switch
		{
			AndroidOutputView.Raw => "Raw Exchange",
			AndroidOutputView.Debug => "Debug Output",
			_ => "Response"
		};
		ResponseTabButton.IsEnabled = outputView != AndroidOutputView.Response;
		RawTabButton.IsEnabled = outputView != AndroidOutputView.Raw;
		DebugTabButton.IsEnabled = outputView != AndroidOutputView.Debug;

		// The "Copy Summary" affordance is only meaningful while the
		// debug pane is in view — outside of that the summary collapses
		// to noise. Hide the pretty-print toggle in the same slot when
		// summary mode takes over so the toolbar stays uncluttered.
		bool isDebug = outputView == AndroidOutputView.Debug;
		CopyDebugSummaryButton.IsVisible = isDebug;
		ResponsePrettyPrintButton.IsVisible = !isDebug;
	}

	private static View BuildFallbackContent(Exception exception)
	{
		return new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(24),
				Spacing = 12,
				Children =
				{
					new Label
					{
						Text = "ForRest failed during Android startup.",
						FontAttributes = FontAttributes.Bold,
						FontSize = 20
					},
					new Label
					{
						Text = $"Diagnostics were written to:{Environment.NewLine}{AppLaunchGuard.StartupLogPath}"
					},
					new Label
					{
						Text = exception.ToString(),
						FontFamily = "OpenSansRegular"
					}
				}
			}
		};
	}

	private enum AndroidOutputView
	{
		Response,
		Raw,
		Debug
	}
}
