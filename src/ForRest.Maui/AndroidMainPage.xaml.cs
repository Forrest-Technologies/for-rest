using ForRest.Maui.Services;
using ForRest.Maui.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ForRest.Maui;

public partial class AndroidMainPage : ContentPage
{
	private bool _isInitialized;
	private AndroidOutputView _outputView = AndroidOutputView.Response;

	public AndroidMainPage()
	{
		InitializeComponent();
		Loaded += OnPageLoaded;
		SizeChanged += OnPageSizeChanged;
		UpdateOutputView(AndroidOutputView.Response);
	}

	private MainPageViewModel? ViewModel => BindingContext as MainPageViewModel;

	private async void OnPageLoaded(object? sender, EventArgs e)
	{
		if (_isInitialized)
		{
			return;
		}

		_isInitialized = true;
		try
		{
			EnsureBindingContext();
			if (ViewModel is null)
			{
				throw new InvalidOperationException("AndroidMainPage could not resolve MainPageViewModel.");
			}

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

	private void EnsureBindingContext()
	{
		if (BindingContext is MainPageViewModel)
		{
			return;
		}

		IServiceProvider? services = Handler?.MauiContext?.Services;
		if (services is null)
		{
			return;
		}

		BindingContext = services.GetService<MainPageViewModel>();
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
