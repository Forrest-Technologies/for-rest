using Microsoft.Maui.ApplicationModel.DataTransfer;
#if ANDROID
using AndroidView = Android.Views.View;
#endif

namespace ForRest.Maui.Controls;

public sealed class CopyableLabelCopiedEventArgs(string text, string message) : EventArgs
{
	public string Text { get; } = text;

	public string Message { get; } = message;
}

public sealed class CopyableLabel : Label
{
	public static readonly BindableProperty CopyTextProperty = BindableProperty.Create(
		nameof(CopyText),
		typeof(string),
		typeof(CopyableLabel),
		string.Empty);

	public static readonly BindableProperty CopiedMessageProperty = BindableProperty.Create(
		nameof(CopiedMessage),
		typeof(string),
		typeof(CopyableLabel),
		string.Empty);

	public event EventHandler<CopyableLabelCopiedEventArgs>? Copied;

#if ANDROID
	private AndroidView? _androidPlatformView;
#endif

	public CopyableLabel()
	{
		HandlerChanged += OnHandlerChanged;
		Unloaded += OnUnloaded;
	}

	public string CopyText
	{
		get => (string)GetValue(CopyTextProperty);
		set => SetValue(CopyTextProperty, value);
	}

	public string CopiedMessage
	{
		get => (string)GetValue(CopiedMessageProperty);
		set => SetValue(CopiedMessageProperty, value);
	}

	private void OnHandlerChanged(object? sender, EventArgs e)
	{
#if ANDROID
		AttachAndroidLongPress();
#endif
	}

	private void OnUnloaded(object? sender, EventArgs e)
	{
#if ANDROID
		DetachAndroidLongPress();
#endif
	}

#if ANDROID
	private void AttachAndroidLongPress()
	{
		AndroidView? platformView = Handler?.PlatformView as AndroidView;
		if (ReferenceEquals(_androidPlatformView, platformView))
		{
			return;
		}

		DetachAndroidLongPress();
		_androidPlatformView = platformView;
		if (_androidPlatformView is null)
		{
			return;
		}

		_androidPlatformView.LongClickable = true;
		_androidPlatformView.LongClick += OnAndroidLongClick;
	}

	private void DetachAndroidLongPress()
	{
		if (_androidPlatformView is null)
		{
			return;
		}

		_androidPlatformView.LongClick -= OnAndroidLongClick;
		_androidPlatformView = null;
	}

	private async void OnAndroidLongClick(object? sender, EventArgs e)
	{
		await CopyAsync();
	}
#endif

	private async Task CopyAsync()
	{
		string copyText = string.IsNullOrWhiteSpace(CopyText) ? Text ?? string.Empty : CopyText;
		if (string.IsNullOrWhiteSpace(copyText))
		{
			return;
		}

		await Clipboard.Default.SetTextAsync(copyText);
		Copied?.Invoke(
			this,
			new CopyableLabelCopiedEventArgs(
				copyText,
				string.IsNullOrWhiteSpace(CopiedMessage) ? "Copied value." : CopiedMessage));
	}
}
