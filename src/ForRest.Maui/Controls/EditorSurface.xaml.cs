using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace ForRest.Maui.Controls;

public partial class EditorSurface : ContentView
{
	public static readonly BindableProperty TitleProperty = BindableProperty.Create(
		nameof(Title),
		typeof(string),
		typeof(EditorSurface),
		"Editor");

	public static readonly BindableProperty LanguageProperty = BindableProperty.Create(
		nameof(Language),
		typeof(string),
		typeof(EditorSurface),
		"plaintext");

	public static readonly BindableProperty TextProperty = BindableProperty.Create(
		nameof(Text),
		typeof(string),
		typeof(EditorSurface),
		string.Empty,
		defaultBindingMode: BindingMode.TwoWay);

	public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
		nameof(IsReadOnly),
		typeof(bool),
		typeof(EditorSurface),
		false);

	public static readonly BindableProperty EditorFontSizeProperty = BindableProperty.Create(
		nameof(EditorFontSize),
		typeof(double),
		typeof(EditorSurface),
		13.5d);

	public static readonly BindableProperty FooterTextProperty = BindableProperty.Create(
		nameof(FooterText),
		typeof(string),
		typeof(EditorSurface),
		string.Empty);

	public static readonly BindableProperty ShowHeaderProperty = BindableProperty.Create(
		nameof(ShowHeader),
		typeof(bool),
		typeof(EditorSurface),
		true);

	public static readonly BindableProperty ShowFooterProperty = BindableProperty.Create(
		nameof(ShowFooter),
		typeof(bool),
		typeof(EditorSurface),
		true);

	public EditorSurface()
	{
		InitializeComponent();
		TextEditor.Focused += OnTextEditorFocused;
		TextEditor.Unfocused += OnTextEditorUnfocused;
	}

	/// <summary>
	/// Forwarded from the inner Maui Editor so the host (e.g. workbench
	/// pane) can react to the user starting / ending an edit session
	/// without reaching into the visual tree. Used by the secret-masking
	/// presentation layer to swap masked and revealed text on focus.
	/// </summary>
	public event EventHandler<FocusEventArgs>? InnerEditorFocused;

	public event EventHandler<FocusEventArgs>? InnerEditorUnfocused;

	public string Title
	{
		get => (string)GetValue(TitleProperty);
		set => SetValue(TitleProperty, value);
	}

	public string Language
	{
		get => (string)GetValue(LanguageProperty);
		set => SetValue(LanguageProperty, value);
	}

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public bool IsReadOnly
	{
		get => (bool)GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	public double EditorFontSize
	{
		get => (double)GetValue(EditorFontSizeProperty);
		set => SetValue(EditorFontSizeProperty, value);
	}

	public string FooterText
	{
		get => (string)GetValue(FooterTextProperty);
		set => SetValue(FooterTextProperty, value);
	}

	public bool ShowHeader
	{
		get => (bool)GetValue(ShowHeaderProperty);
		set => SetValue(ShowHeaderProperty, value);
	}

	public bool ShowFooter
	{
		get => (bool)GetValue(ShowFooterProperty);
		set => SetValue(ShowFooterProperty, value);
	}

	public async Task<bool> PasteFromClipboardAsync()
	{
		if (IsReadOnly)
		{
			return false;
		}

		string? clipboardText = null;
		try
		{
			clipboardText = await Clipboard.Default.GetTextAsync();
		}
		catch
		{
			return false;
		}

		if (string.IsNullOrEmpty(clipboardText))
		{
			return false;
		}

		string currentText = Text ?? string.Empty;
		int cursorPosition = Math.Clamp(TextEditor.CursorPosition, 0, currentText.Length);
		int selectionLength = Math.Clamp(TextEditor.SelectionLength, 0, currentText.Length - cursorPosition);
		string nextText = currentText.Remove(cursorPosition, selectionLength).Insert(cursorPosition, clipboardText);
		Text = nextText;

		int nextCursorPosition = Math.Clamp(cursorPosition + clipboardText.Length, 0, nextText.Length);
		TextEditor.Focus();
		TextEditor.CursorPosition = nextCursorPosition;
		TextEditor.SelectionLength = 0;
		return true;
	}

	private void OnTextEditorFocused(object? sender, FocusEventArgs e)
	{
		InnerEditorFocused?.Invoke(this, e);
	}

	private void OnTextEditorUnfocused(object? sender, FocusEventArgs e)
	{
		InnerEditorUnfocused?.Invoke(this, e);
	}

}
