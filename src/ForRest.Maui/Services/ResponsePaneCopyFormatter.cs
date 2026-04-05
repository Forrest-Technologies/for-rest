using System.Collections.Generic;
using System.Linq;
using System.Text;
using ForRest.Maui.ViewModels;

namespace ForRest.Maui.Services;

public static class ResponsePaneCopyFormatter
{
	public static string BuildHeadersText(IEnumerable<NameValueRowViewModel> rows)
	{
		return BuildTable(
			["Header", "Value"],
			rows.Select(static row => new[] { row.Name, row.Value }));
	}

	public static string BuildTraceText(IEnumerable<TraceEntryViewModel> entries)
	{
		return BuildTable(
			["When", "Step", "Detail"],
			entries.Select(static entry => new[] { entry.When, entry.Title, entry.Detail }));
	}

	public static string BuildStashText(IReadOnlyList<StashColumnViewModel> columns, IEnumerable<StashRowViewModel> rows)
	{
		if (columns.Count == 0)
		{
			return string.Empty;
		}

		return BuildTable(
			columns.Select(static column => column.Title),
			rows.Select(static row => row.Cells.Select(static cell => cell.Value)));
	}

	public static string BuildStashRowText(IReadOnlyList<StashColumnViewModel> columns, StashRowViewModel row)
	{
		if (columns.Count == 0)
		{
			return string.Empty;
		}

		return BuildTable(
			["#", .. columns.Select(static column => column.Title)],
			[[row.RowLabel, .. row.Cells.Select(static cell => cell.Value)]]);
	}

	private static string BuildTable(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
	{
		List<string> headerCells = headers.Select(NormalizeCell).ToList();
		if (headerCells.Count == 0)
		{
			return string.Empty;
		}

		StringBuilder builder = new();
		builder.AppendLine(string.Join('\t', headerCells));
		foreach (IEnumerable<string> row in rows)
		{
			builder.AppendLine(string.Join('\t', row.Select(NormalizeCell)));
		}

		return builder.ToString().TrimEnd();
	}

	private static string NormalizeCell(string? value)
	{
		string normalized = ResponsePresentationFormatter.NormalizeDisplayText(value ?? string.Empty);
		return normalized
			.Replace("\t", "    ", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal);
	}
}
