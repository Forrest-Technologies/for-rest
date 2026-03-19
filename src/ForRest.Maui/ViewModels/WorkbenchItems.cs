using System.Collections.ObjectModel;
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

public sealed class RequestDocumentViewModel(
	string title,
	string method,
	string summary,
	string location,
	bool isDirty,
	bool isSelected = false) : ObservableObject
{
	private bool _isSelected = isSelected;

	public string Title { get; } = title;

	public string Method { get; } = method;

	public string Summary { get; } = summary;

	public string Location { get; } = location;

	public bool IsDirty { get; } = isDirty;

	public string DisplayTitle => IsDirty ? $"{Title} *" : Title;

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
}

public sealed class NavigationSectionViewModel(string title, IEnumerable<NavigationItemViewModel> items)
{
	public string Title { get; } = title;

	public ObservableCollection<NavigationItemViewModel> Items { get; } = new(items);
}

public sealed class NavigationItemViewModel(
	string kind,
	string title,
	string detail,
	string context,
	Color accentColor,
	string? method = null,
	int depth = 0,
	bool isSelected = false) : ObservableObject
{
	private bool _isSelected = isSelected;

	public string Kind { get; } = kind;

	public string Title { get; } = title;

	public string Detail { get; } = detail;

	public string Context { get; } = context;

	public Color AccentColor { get; } = accentColor;

	public string? Method { get; } = method;

	public int Depth { get; } = depth;

	public Microsoft.Maui.Thickness Indent => new(12 + (Depth * 14), 0, 12, 0);

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
}

public sealed class HistoryEntryViewModel(string method, string title, string summary, string when, Color accentColor)
{
	public string Method { get; } = method;

	public string Title { get; } = title;

	public string Summary { get; } = summary;

	public string When { get; } = when;

	public Color AccentColor { get; } = accentColor;
}

public sealed class NameValueRowViewModel(string name, string value, string scope, bool isEnabled = true)
{
	public string Name { get; } = name;

	public string Value { get; } = value;

	public string Scope { get; } = scope;

	public bool IsEnabled { get; } = isEnabled;
}

public sealed class OutputMetricViewModel(string label, string value, Color accentColor)
{
	public string Label { get; } = label;

	public string Value { get; } = value;

	public Color AccentColor { get; } = accentColor;
}

public sealed class TraceEntryViewModel(string title, string detail, string when, Color accentColor)
{
	public string Title { get; } = title;

	public string Detail { get; } = detail;

	public string When { get; } = when;

	public Color AccentColor { get; } = accentColor;
}
