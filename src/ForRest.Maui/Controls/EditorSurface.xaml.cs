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

	public EditorSurface()
	{
		InitializeComponent();
	}

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
}
