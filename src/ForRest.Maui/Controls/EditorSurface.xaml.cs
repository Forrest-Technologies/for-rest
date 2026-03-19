using System.Collections.ObjectModel;
using System.Linq;

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
		defaultBindingMode: BindingMode.TwoWay,
		propertyChanged: OnTextChanged);

	public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
		nameof(IsReadOnly),
		typeof(bool),
		typeof(EditorSurface),
		false);

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
		UpdateLineNumbers(Text);
	}

	public ObservableCollection<string> LineNumbers { get; } = [];

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

	private static void OnTextChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		((EditorSurface)bindable).UpdateLineNumbers(newValue as string);
	}

	private void UpdateLineNumbers(string? text)
	{
		int lineCount = 1;

		if (!string.IsNullOrEmpty(text))
		{
			lineCount = text.Count(character => character == '\n') + 1;
		}

		while (LineNumbers.Count < lineCount)
		{
			LineNumbers.Add((LineNumbers.Count + 1).ToString());
		}

		while (LineNumbers.Count > lineCount)
		{
			LineNumbers.RemoveAt(LineNumbers.Count - 1);
		}
	}
}
