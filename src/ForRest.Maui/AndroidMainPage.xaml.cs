using ForRest.Maui.Services;
using ForRest.Maui.ViewModels;
using Microsoft.Maui.ApplicationModel.DataTransfer;

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

	private MainPageViewModel ViewModel => (MainPageViewModel)BindingContext;

	private async void OnPageLoaded(object? sender, EventArgs e)
	{
		if (_isInitialized)
		{
			return;
		}

		_isInitialized = true;
		try
		{
			ViewModel.UpdateLayoutMode(Width);
			await ViewModel.InitializeAsync();
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
		ViewModel.UpdateLayoutMode(Width);
	}

	private async void OnSendClicked(object? sender, EventArgs e)
	{
		await ExecuteWithLaunchGuard(ViewModel.SendAsync, "Android send failed.");
	}

	private async void OnCopyRequestClicked(object? sender, EventArgs e)
	{
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
					string debugText = ViewModel.DebugOutputText ?? string.Empty;
					if (string.IsNullOrWhiteSpace(debugText))
					{
						ViewModel.ExecutionStatus = "No debug output available to copy.";
						return;
					}

					await Clipboard.Default.SetTextAsync(debugText);
					ViewModel.ExecutionStatus = "Copied debug output.";
					break;
			}
		}
		catch (Exception exception)
		{
			AppLaunchGuard.RecordException("Android output copy failed.", exception);
		}
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
		ResponseBodyEditor.IsVisible = outputView == AndroidOutputView.Response;
		ResponseRawEditor.IsVisible = outputView == AndroidOutputView.Raw;
		ResponseDebugEditor.IsVisible = outputView == AndroidOutputView.Debug;
		OutputTitleLabel.Text = outputView switch
		{
			AndroidOutputView.Raw => "Raw Exchange",
			AndroidOutputView.Debug => "Debug Output",
			_ => "Response"
		};
		ResponseTabButton.IsEnabled = outputView != AndroidOutputView.Response;
		RawTabButton.IsEnabled = outputView != AndroidOutputView.Raw;
		DebugTabButton.IsEnabled = outputView != AndroidOutputView.Debug;
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
