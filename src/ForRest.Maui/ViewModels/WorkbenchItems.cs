using System.Collections.ObjectModel;
using ForRest.Models;
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

public sealed class WorkspaceItemViewModel(Guid id, string title, string subtitle, bool isSelected = false) : ObservableObject
{
	private string _title = title;
	private string _subtitle = subtitle;
	private bool _isSelected = isSelected;

	public Guid Id { get; } = id;

	public string Title
	{
		get => _title;
		set => SetProperty(ref _title, value);
	}

	public string Subtitle
	{
		get => _subtitle;
		set => SetProperty(ref _subtitle, value);
	}

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
	private string _title = title;
	private string _method = method;
	private string _summary = summary;
	private string _location = location;
	private bool _isDirty = isDirty;
	private bool _isSelected = isSelected;

	public string Title
	{
		get => _title;
		set
		{
			if (SetProperty(ref _title, value))
			{
				OnPropertyChanged(nameof(DisplayTitle));
			}
		}
	}

	public string Method
	{
		get => _method;
		set => SetProperty(ref _method, value);
	}

	public string Summary
	{
		get => _summary;
		set => SetProperty(ref _summary, value);
	}

	public string Location
	{
		get => _location;
		set => SetProperty(ref _location, value);
	}

	public bool IsDirty
	{
		get => _isDirty;
		set
		{
			if (SetProperty(ref _isDirty, value))
			{
				OnPropertyChanged(nameof(DisplayTitle));
			}
		}
	}

	public string DisplayTitle => IsDirty ? $"{Title} *" : Title;

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
}

public sealed class NavigationSectionViewModel(
	string title,
	IEnumerable<NavigationItemViewModel> items,
	string subtitle = "",
	bool supportsRequestActions = false)
{
	public string Title { get; } = title;

	public ObservableCollection<NavigationItemViewModel> Items { get; } = new(items);

	public string Subtitle { get; } = subtitle;

	public bool SupportsRequestActions { get; } = supportsRequestActions;
}

public sealed class NavigationItemViewModel(
	string kind,
	string title,
	string detail,
	string context,
	Color accentColor,
	string? method = null,
	int depth = 0,
	bool isSelected = false,
	string documentKind = "request",
	string editorLanguage = "forrest") : ObservableObject
{
	private bool _isSelected = isSelected;
	private Color _accentColor = accentColor;
	private string _title = title;
	private string _detail = detail;

	public string Kind { get; } = kind;

	public string Title
	{
		get => _title;
		set => SetProperty(ref _title, value);
	}

	public string Detail
	{
		get => _detail;
		set => SetProperty(ref _detail, value);
	}

	public string Context { get; } = context;

	public string ContextDisplay => FormatContext(Context);

	public Color AccentColor
	{
		get => _accentColor;
		set => SetProperty(ref _accentColor, value);
	}

	public string? Method { get; } = method;

	public int Depth { get; } = depth;

	public string DocumentKind { get; } = documentKind;

	public string EditorLanguage { get; } = editorLanguage;

	public Microsoft.Maui.Thickness Indent => new(12 + (Depth * 14), 0, 12, 0);

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}

	private static string FormatContext(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "~";
		}

		return value.StartsWith("/", StringComparison.Ordinal)
			? value[1..]
			: value;
	}
}

public sealed class HistoryEntryViewModel(
	ExecutionRun run,
	string method,
	string title,
	string summary,
	string when,
	Color accentColor,
	bool isSelected = false) : ObservableObject
{
	private Color _accentColor = accentColor;
	private bool _isSelected = isSelected;

	public ExecutionRun Run { get; } = run;

	public string Method { get; } = method;

	public string Title { get; } = title;

	public string Summary { get; } = summary;

	public string When { get; } = when;

	public Color AccentColor
	{
		get => _accentColor;
		set => SetProperty(ref _accentColor, value);
	}

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
}

public sealed class NameValueRowViewModel(string name, string value, string scope, bool isEnabled = true)
{
	public string Name { get; } = name;

	public string Value { get; } = value;

	public string Scope { get; } = scope;

	public bool IsEnabled { get; } = isEnabled;
}

public sealed class OutputMetricViewModel(string label, string value, Color accentColor) : ObservableObject
{
	private Color _accentColor = accentColor;

	public string Label { get; } = label;

	public string Value { get; } = value;

	public Color AccentColor
	{
		get => _accentColor;
		set => SetProperty(ref _accentColor, value);
	}
}

public sealed class TraceEntryViewModel(string title, string detail, string when, Color accentColor) : ObservableObject
{
	private Color _accentColor = accentColor;

	public string Title { get; } = title;

	public string Detail { get; } = detail;

	public string When { get; } = when;

	public Color AccentColor
	{
		get => _accentColor;
		set => SetProperty(ref _accentColor, value);
	}
}

public sealed class StashColumnViewModel(string title)
{
	public string Title { get; } = title;
}

public sealed class StashCellViewModel(string value)
{
	public string Value { get; } = value;
}

public sealed class StashRowViewModel(IEnumerable<StashCellViewModel> cells)
{
	public ObservableCollection<StashCellViewModel> Cells { get; } = new(cells);
}

public sealed class LanguageHelpEntryViewModel(
	string key,
	string title,
	string category,
	string summary,
	string documentation,
	string example,
	IReadOnlyList<string> searchTerms)
{
	public string Key { get; } = key;

	public string Title { get; } = title;

	public string Category { get; } = category;

	public string Summary { get; } = summary;

	public string Documentation { get; } = documentation;

	public string Example { get; } = example;

	public IReadOnlyList<string> SearchTerms { get; } = searchTerms;

	public bool MatchesSearch(string query)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return true;
		}

		return Title.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| Category.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| Summary.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| Documentation.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| SearchTerms.Any(term => term.Contains(query, StringComparison.OrdinalIgnoreCase));
	}
}
