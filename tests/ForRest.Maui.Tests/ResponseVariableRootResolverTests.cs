using ForRest.Maui.Services;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class ResponseVariableRootResolverTests
{
	[TestMethod]
	public void Resolve_returns_in_scope_send_variable_for_nested_block()
	{
		string source =
			"""
			name "Users"
			method GET
			url "https://example.test/users"

			let sent = request.send()
			if sent.status == 200 {
			  let inner = request.send()
			  log inner.body
			}
			""";

		string root = ResponseVariableRootResolver.Resolve(source, 7);

		Assert.AreEqual("inner", root);
	}

	[TestMethod]
	public void Resolve_falls_back_to_outer_send_variable_after_nested_block_ends()
	{
		string source =
			"""
			name "Users"
			method GET
			url "https://example.test/users"

			let sent = request.send()
			if sent.status == 200 {
			  let inner = request.send()
			  log inner.body
			}

			log sent.status
			""";

		string root = ResponseVariableRootResolver.Resolve(source, 10);

		Assert.AreEqual("sent", root);
	}

	[TestMethod]
	public void Resolve_supports_reassignment_without_redeclaration()
	{
		string source =
			"""
			name "Users"
			method GET
			url "https://example.test/users"

			let sent = null
			sent = request.send()
			log sent.status
			""";

		string root = ResponseVariableRootResolver.Resolve(source, 7);

		Assert.AreEqual("sent", root);
	}

	[TestMethod]
	public void Resolve_returns_default_root_when_no_send_capture_exists()
	{
		string source =
			"""
			name "Users"
			method GET
			url "https://example.test/users"

			log response.status
			""";

		string root = ResponseVariableRootResolver.Resolve(source, 5);

		Assert.AreEqual("response", root);
	}
}
