using Microsoft.Maui.Graphics;

namespace ForRest.Maui.ViewModels;

public sealed class PaneTabViewModel(string key, string title, bool isSelected = false) : ObservableObject
{
	private bool _isSelected = isSelected;

	public string Key { get; } = key;

	public string Title { get; } = title;

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
}

public sealed class RequestDocumentViewModel(string title, string method, string summary, bool isDirty, bool isSelected = false) : ObservableObject
{
	private bool _isSelected = isSelected;

	public string Title { get; } = title;

	public string Method { get; } = method;

	public string Summary { get; } = summary;

	public bool IsDirty { get; } = isDirty;

	public string DisplayTitle => IsDirty ? $"{Title} *" : Title;

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
}

public sealed class ExplorerItemViewModel(
	string kind,
	string title,
	string detail,
	Color badgeBackground,
	bool isSelected = false) : ObservableObject
{
	private bool _isSelected = isSelected;

	public string Kind { get; } = kind;

	public string Title { get; } = title;

	public string Detail { get; } = detail;

	public Color BadgeBackground { get; } = badgeBackground;

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
}

public sealed class HistoryEntryViewModel(string method, string title, string summary, string when, Color badgeBackground)
{
	public string Method { get; } = method;

	public string Title { get; } = title;

	public string Summary { get; } = summary;

	public string When { get; } = when;

	public Color BadgeBackground { get; } = badgeBackground;
}

public sealed class NameValueRowViewModel(string name, string value, string scope, bool isEnabled = true)
{
	public string Name { get; } = name;

	public string Value { get; } = value;

	public string Scope { get; } = scope;

	public bool IsEnabled { get; } = isEnabled;
}
