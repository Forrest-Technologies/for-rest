using ForRest.Maui.Services;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class ResponseVariableExpressionServiceTests
{
	[TestMethod]
	public void TryBuildExpression_returns_nested_path_for_array_item_property()
	{
		string json =
			"""
			{
			  "items": [
			    {
			      "id": 42,
			      "profile": {
			        "displayName": "Ada"
			      }
			    }
			  ]
			}
			""";

		bool succeeded = ResponseVariableExpressionService.TryBuildExpression(json, 6, 10, out string expression);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("response.items[0].profile.displayName", expression);
	}

	[TestMethod]
	public void TryBuildExpression_uses_bracket_notation_for_reserved_root_members()
	{
		string json =
			"""
			{
			  "status": {
			    "value": "ok"
			  }
			}
			""";

		bool succeeded = ResponseVariableExpressionService.TryBuildExpression(json, 2, 5, out string expression);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("response[\"status\"]", expression);
	}

	[TestMethod]
	public void TryBuildExpression_supports_property_names_with_special_characters()
	{
		string json =
			"""
			{
			  "device-info": {
			    "trace_id": "abc"
			  }
			}
			""";

		bool succeeded = ResponseVariableExpressionService.TryBuildExpression(json, 2, 5, out string expression);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("response[\"device-info\"]", expression);
	}

	[TestMethod]
	public void TryBuildExpression_handles_display_text_with_literal_newlines_inside_strings()
	{
		string json =
			"""
			{
			  "message": "line 1
			line 2",
			  "traceId": "abc"
			}
			""";

		bool succeeded = ResponseVariableExpressionService.TryBuildExpression(json, 4, 5, out string expression);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("response.traceId", expression);
	}

	[TestMethod]
	public void TryBuildExpression_returns_false_when_cursor_is_not_on_property_name()
	{
		string json =
			"""
			{
			  "message": "ok"
			}
			""";

		bool succeeded = ResponseVariableExpressionService.TryBuildExpression(json, 2, 18, out string expression);

		Assert.IsFalse(succeeded);
		Assert.AreEqual(string.Empty, expression);
	}
}
