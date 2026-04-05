using System;
using ForRest.Maui.Services;
using ForRest.Maui.ViewModels;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class ResponsePaneCopyFormatterTests
{
	[TestMethod]
	public void BuildHeadersText_formats_headers_as_tabular_text()
	{
		NameValueRowViewModel[] rows =
		[
			new("Content-Type", "application/json", "response"),
			new("X-Trace", "line1\r\nline2", "response")
		];

		string text = ResponsePaneCopyFormatter.BuildHeadersText(rows);

		Assert.AreEqual(
			$"Header\tValue{Environment.NewLine}Content-Type\tapplication/json{Environment.NewLine}X-Trace\tline1\\nline2",
			text);
	}

	[TestMethod]
	public void BuildStashText_flattens_rows_into_tsv()
	{
		StashColumnViewModel[] columns =
		[
			new("Index"),
			new("Token")
		];
		StashRowViewModel[] rows =
		[
			new(
				1,
				[new StashCellViewModel("0"), new StashCellViewModel("abc\t123")],
				[new NameValueRowViewModel("Index", "0", "stash"), new NameValueRowViewModel("Token", "abc\t123", "stash")],
				isAlternate: false,
				searchText: "1 Index 0 Token abc 123"),
			new(
				2,
				[new StashCellViewModel("1"), new StashCellViewModel("line1\nline2")],
				[new NameValueRowViewModel("Index", "1", "stash"), new NameValueRowViewModel("Token", "line1\nline2", "stash")],
				isAlternate: true,
				searchText: "2 Index 1 Token line1 line2")
		];

		string text = ResponsePaneCopyFormatter.BuildStashText(columns, rows);

		Assert.AreEqual(
			$"Index\tToken{Environment.NewLine}0\tabc    123{Environment.NewLine}1\tline1\\nline2",
			text);
	}

	[TestMethod]
	public void BuildStashRowText_includes_row_number_and_values()
	{
		IReadOnlyList<StashColumnViewModel> columns =
		[
			new("Method"),
			new("Token")
		];
		StashRowViewModel row = new(
			2,
			[
				new StashCellViewModel("POST"),
				new StashCellViewModel("beta")
			],
			[
				new NameValueRowViewModel("Method", "POST", "stash"),
				new NameValueRowViewModel("Token", "beta", "stash")
			],
			isAlternate: true,
			searchText: "2 Method POST Token beta");

		string text = ResponsePaneCopyFormatter.BuildStashRowText(columns, row);

		StringAssert.Contains(text, "#\tMethod\tToken");
		StringAssert.Contains(text, "2\tPOST\tbeta");
	}
}
