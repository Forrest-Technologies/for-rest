using System;
using ForRest.Maui.Services;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class ResponsePresentationFormatterTests
{
	[TestMethod]
	public void FormatBody_pretty_prints_json_before_normalizing_escaped_newlines()
	{
		string formatted = ResponsePresentationFormatter.FormatBody(
			"""{"message":"line 1\r\nline 2","items":[{"id":1},{"id":2}]}""",
			prettyPrintEnabled: true);

		StringAssert.Contains(formatted, "\"items\": [");
		StringAssert.Contains(formatted, "\"id\": 1");
		Assert.IsFalse(formatted.Contains("\\r\\n", StringComparison.Ordinal));
		Assert.IsFalse(formatted.Contains("\\n", StringComparison.Ordinal));
		StringAssert.Contains(formatted, "line 1\nline 2");
	}

	[TestMethod]
	public void FormatBody_returns_normalized_text_when_body_is_not_json()
	{
		string formatted = ResponsePresentationFormatter.FormatBody(
			"first\\r\\nsecond\\nthird",
			prettyPrintEnabled: true);

		Assert.AreEqual("first\nsecond\nthird", formatted);
	}

	[TestMethod]
	public void NormalizeDisplayText_decodes_escaped_and_actual_newlines()
	{
		string normalized = ResponsePresentationFormatter.NormalizeDisplayText("one\\r\\ntwo\r\nthree\\nfour\rfive");

		Assert.AreEqual("one\ntwo\nthree\nfour\nfive", normalized);
	}
}
