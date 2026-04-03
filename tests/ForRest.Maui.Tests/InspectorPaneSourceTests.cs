using System.IO;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class InspectorPaneSourceTests
{
	[TestMethod]
	public void ResponseSnapshotRail_uses_a_horizontal_scroll_view_instead_of_a_collection_view()
	{
		string xaml = GetSource("src", "ForRest.Maui", "Controls", "InspectorPane.xaml");

		StringAssert.Contains(xaml, "x:Name=\"ResponseSnapshotRail\"");
		StringAssert.Contains(xaml, "Orientation=\"Horizontal\"");
		StringAssert.Contains(xaml, "HorizontalScrollBarVisibility=\"Always\"");
		StringAssert.Contains(xaml, "BindableLayout.ItemsSource=\"{Binding ResponseSnapshotEntries}\"");
		StringAssert.Contains(xaml, "Text=\"Prev\"");
		StringAssert.Contains(xaml, "Text=\"Next\"");
		Assert.IsFalse(xaml.Contains("<CollectionView Grid.Row=\"1\"", StringComparison.Ordinal));
	}

	[TestMethod]
	public void ResponseSnapshotRail_translates_mouse_wheel_input_into_horizontal_scrolling_on_windows()
	{
		string source = GetSource("src", "ForRest.Maui", "Controls", "InspectorPane.xaml.cs").Replace("\r\n", "\n", StringComparison.Ordinal);

		StringAssert.Contains(source, "EnsureResponseSnapshotRailInteraction()");
		StringAssert.Contains(source, "PointerWheelChanged += OnResponseSnapshotRailPointerWheelChanged;");
		StringAssert.Contains(source, "KeyDown += OnResponseSnapshotRailKeyDown;");
		StringAssert.Contains(source, "FocusResponseSnapshotRail();");
		StringAssert.Contains(source, "platformView.ChangeView(targetOffset, null, null, true);");
		StringAssert.Contains(source, "ViewModel.SelectPreviousResponseSnapshot()");
		StringAssert.Contains(source, "ViewModel.SelectNextResponseSnapshot()");
		StringAssert.Contains(source, "ViewModel.SelectedResponseSnapshotEntry = entry;");
	}

	[TestMethod]
	public void RequestSnapshotRail_uses_a_horizontal_scroll_view_with_request_specific_handlers()
	{
		string xaml = GetSource("src", "ForRest.Maui", "Controls", "InspectorPane.xaml");

		StringAssert.Contains(xaml, "x:Name=\"RequestSnapshotRail\"");
		StringAssert.Contains(xaml, "Orientation=\"Horizontal\"");
		StringAssert.Contains(xaml, "HorizontalScrollBarVisibility=\"Always\"");
		StringAssert.Contains(xaml, "BindableLayout.ItemsSource=\"{Binding RequestSnapshotEntries}\"");
		StringAssert.Contains(xaml, "Clicked=\"OnPreviousRequestSnapshotClicked\"");
		StringAssert.Contains(xaml, "Clicked=\"OnNextRequestSnapshotClicked\"");
		StringAssert.Contains(xaml, "Clicked=\"OnCopyRequestClicked\"");
		StringAssert.Contains(xaml, "Clicked=\"OnCopyRawRequestClicked\"");
		StringAssert.Contains(xaml, "Tapped=\"OnRequestSnapshotTapped\"");
	}

	[TestMethod]
	public void RequestSnapshotRail_translates_mouse_wheel_input_into_horizontal_scrolling_on_windows()
	{
		string source = GetSource("src", "ForRest.Maui", "Controls", "InspectorPane.xaml.cs").Replace("\r\n", "\n", StringComparison.Ordinal);

		StringAssert.Contains(source, "EnsureRequestSnapshotRailInteraction()");
		StringAssert.Contains(source, "PointerWheelChanged += OnRequestSnapshotRailPointerWheelChanged;");
		StringAssert.Contains(source, "KeyDown += OnRequestSnapshotRailKeyDown;");
		StringAssert.Contains(source, "FocusRequestSnapshotRail();");
		StringAssert.Contains(source, "platformView.ChangeView(targetOffset, null, null, true);");
		StringAssert.Contains(source, "ViewModel.SelectPreviousRequestSnapshot()");
		StringAssert.Contains(source, "ViewModel.SelectNextRequestSnapshot()");
		StringAssert.Contains(source, "ViewModel.SelectedRequestSnapshotEntry = entry;");
		StringAssert.Contains(source, "await ViewModel.CopyRequestBodyAsync();");
		StringAssert.Contains(source, "await ViewModel.CopyRawRequestAsync();");
	}

	private static string GetSource(params string[] segments)
	{
		string sourcePath = Path.GetFullPath(
			Path.Combine(
				[
					AppContext.BaseDirectory,
					"..",
					"..",
					"..",
					"..",
					"..",
					..segments
				]));
		return File.ReadAllText(sourcePath);
	}
}
