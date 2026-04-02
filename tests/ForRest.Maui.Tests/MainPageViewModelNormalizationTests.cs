using System;
using ForRest.Maui.Services;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class MainPageViewModelNormalizationTests
{
	[TestMethod]
	public void NormalizeLoadedWorkbenchState_converts_legacy_section_documents_to_code_first_source()
	{
		RequestWorkbenchState legacyState = new()
		{
			SelectedWorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Workspaces =
			[
				new()
				{
					Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
					Name = "Legacy",
					SelectedEnvironment = "Local",
					SelectedDocumentLocation = "/requests/legacy/users",
					Documents =
					[
						new()
						{
							Title = "Users Feed",
							Method = "GET",
							Summary = "Legacy request",
							Location = "/requests/legacy/users",
							RequestSource =
							"""
							meta {
							  name = "Users Feed"
							}

							vars {
							  request resource_id = "42"
							  runtime trace_id = guid()
							}

							request {
							  method = GET
							  url = "https://httpbin.org/anything/users/{{resource_id}}?trace={{trace_id}}"
							  history = true
							}

							headers {
							  Accept = "application/json"
							  X-Correlation-Id = "{{trace_id}}"
							}

							tests {
							  status == 200 "returns 200"
							}

							retry {
							  count = 1
							  interval = 250
							}
							""",
							PreRequestScript = string.Empty
						}
					]
				}
			]
		};

		RequestWorkbenchState normalized = RequestWorkbenchDocumentNormalizer.NormalizeLoadedWorkbenchState(legacyState, out bool changed);

		Assert.IsTrue(changed);
		StringAssert.Contains(normalized.Workspaces[0].Documents[0].RequestSource, "name \"Users Feed\"");
		StringAssert.Contains(normalized.Workspaces[0].Documents[0].RequestSource, "header \"Accept\" = \"application/json\"");
		StringAssert.Contains(normalized.Workspaces[0].Documents[0].RequestSource, "expect status == 200 \"returns 200\"");
		StringAssert.Contains(normalized.Workspaces[0].Documents[0].RequestSource, "retry count = 1");
		Assert.IsFalse(normalized.Workspaces[0].Documents[0].RequestSource.Contains("headers {", StringComparison.Ordinal));
		Assert.IsFalse(normalized.Workspaces[0].Documents[0].RequestSource.Contains("retry {", StringComparison.Ordinal));
	}

	[TestMethod]
	public void NormalizeLoadedWorkbenchState_repairs_mixed_code_first_documents_with_legacy_flow_lines()
	{
		RequestWorkbenchState legacyState = new()
		{
			SelectedWorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Workspaces =
			[
				new()
				{
					Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
					Name = "Legacy",
					SelectedEnvironment = "Local",
					SelectedDocumentLocation = "/requests/legacy/users",
					Documents =
					[
						new()
						{
							Title = "Sync Profile",
							Method = "POST",
							Summary = "Mixed request",
							Location = "/requests/legacy/users",
							RequestSource =
							"""
							name "Sync Profile"
							method POST
							url "https://httpbin.org/anything/users/{{user_id}}"

							runtime request_name = "Sync Profile");
							request user_id = "42";
							header "Accept" = "application/json";
							request.SetHeader("X-Shell-Surface", "editor-first");
							console.Log("Prepared request before send.");
							let sent = request.send();

							expect status == 200 "returns 200";
							""",
							PreRequestScript = string.Empty
						}
					]
				}
			]
		};

		RequestWorkbenchState normalized = RequestWorkbenchDocumentNormalizer.NormalizeLoadedWorkbenchState(legacyState, out bool changed);

		Assert.IsTrue(changed);
		StringAssert.Contains(normalized.Workspaces[0].Documents[0].RequestSource, "runtime request_name = \"Sync Profile\"");
		StringAssert.Contains(normalized.Workspaces[0].Documents[0].RequestSource, "request.headers[\"X-Shell-Surface\"] = \"editor-first\"");
		StringAssert.Contains(normalized.Workspaces[0].Documents[0].RequestSource, "log \"Prepared request before send.\"");
		Assert.IsFalse(normalized.Workspaces[0].Documents[0].RequestSource.Contains("console.Log(", StringComparison.Ordinal));
		Assert.IsFalse(normalized.Workspaces[0].Documents[0].RequestSource.Contains("\");", StringComparison.Ordinal));
	}

	[TestMethod]
	public void NormalizeLoadedWorkbenchState_repairs_legacy_flow_lines_even_when_semicolons_are_missing()
	{
		RequestWorkbenchState legacyState = new()
		{
			SelectedWorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Workspaces =
			[
				new()
				{
					Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
					Name = "Legacy",
					SelectedEnvironment = "Local",
					SelectedDocumentLocation = "/requests/users/sync",
					Documents =
					[
						new()
						{
							Title = "Sync Profile",
							Method = "PUT",
							Summary = "Mixed request",
							Location = "/requests/users/sync",
							RequestSource =
							"""
							name "Sync Profile"
							method PUT
							url "https://httpbin.org/anything/requests/users/sync/{{resource_id}}?trace={{trace_id}}"
							max_send_iterations 1

							request resource_id = “42”
							runtime trace_id = guid()

							request.SetHeader("X-Shell-Surface", "editor-first")
							console.Log("Prepared request before send.")
							var sent = await request.send()

							expect status == 200 "returns 200"
							""",
							PreRequestScript = string.Empty
						}
					]
				}
			]
		};

		RequestWorkbenchState normalized = RequestWorkbenchDocumentNormalizer.NormalizeLoadedWorkbenchState(legacyState, out bool changed);
		string requestSource = normalized.Workspaces[0].Documents[0].RequestSource;

		Assert.IsTrue(changed);
		StringAssert.Contains(requestSource, "request resource_id = \"42\"");
		StringAssert.Contains(requestSource, "request.headers[\"X-Shell-Surface\"] = \"editor-first\"");
		StringAssert.Contains(requestSource, "log \"Prepared request before send.\"");
		StringAssert.Contains(requestSource, "let sent = request.send()");
		Assert.IsFalse(requestSource.Contains("request.SetHeader(", StringComparison.Ordinal));
		Assert.IsFalse(requestSource.Contains("console.Log(", StringComparison.Ordinal));
		Assert.IsFalse(requestSource.Contains("await request.send()", StringComparison.Ordinal));
	}

	[TestMethod]
	public void NormalizeLoadedWorkbenchState_merges_legacy_pre_request_script_into_code_first_flow()
	{
		RequestWorkbenchState legacyState = new()
		{
			SelectedWorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Workspaces =
			[
				new()
				{
					Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
					Name = "Legacy",
					SelectedEnvironment = "Local",
					SelectedDocumentLocation = "/requests/legacy/users",
					Documents =
					[
						new()
						{
							Title = "Probe",
							Method = "GET",
							Summary = "Code-first request",
							Location = "/requests/legacy/users",
							RequestSource =
							"""
							name "Probe"
							method GET
							url "https://httpbin.org/anything"

							let sent = request.send()
							expect status == 200 "returns 200"
							""",
							PreRequestScript =
							"""
							request.SetHeader("X-Legacy", "1");
							console.Log("legacy flow merged");
							"""
						}
					]
				}
			]
		};

		RequestWorkbenchState normalized = RequestWorkbenchDocumentNormalizer.NormalizeLoadedWorkbenchState(legacyState, out bool changed);

		Assert.IsTrue(changed);
		StringAssert.Contains(normalized.Workspaces[0].Documents[0].RequestSource, "request.headers[\"X-Legacy\"] = \"1\"");
		StringAssert.Contains(normalized.Workspaces[0].Documents[0].RequestSource, "log \"legacy flow merged\"");
		Assert.AreEqual(string.Empty, normalized.Workspaces[0].Documents[0].PreRequestScript);
	}

	[TestMethod]
	public void NormalizeRequestDocumentSource_repairs_legacy_request_send_variants_with_spacing_and_trailing_closers()
	{
		string source =
			"""
			name "Users Feed"
			method GET
			url "https://httpbin.org/anything/requests/users/list/{{resource_id}}?trace={{trace_id}}"
			max_send_iterations 3

			request resource_id = "42"
			runtime trace_id = guid()

			request.SetHeader("X-Request-Source", "maui")
			var sent = await request.send() ))
			console.Log(sent[0].email)

			expect status == 200 "returns 200"
			""";

		string normalized = RequestWorkbenchDocumentNormalizer.NormalizeRequestDocumentSource(source, string.Empty, "Users Feed");

		StringAssert.Contains(normalized, "request.headers[\"X-Request-Source\"] = \"maui\"");
		StringAssert.Contains(normalized, "let sent = request.send()");
		StringAssert.Contains(normalized, "log sent[0].email");
		Assert.IsFalse(normalized.Contains("await request.send", StringComparison.Ordinal));
		Assert.IsFalse(normalized.Contains("request.SetHeader(", StringComparison.Ordinal));
		Assert.IsFalse(normalized.Contains("console.Log(", StringComparison.Ordinal));
	}

	[TestMethod]
	public void NormalizeRequestDocumentSource_repairs_truncated_legacy_helper_calls_in_code_first_documents()
	{
		string source =
			"""
			name "Users Feed"
			method GET
			url "https://httpbin.org/anything/requests/users/list/{{resource_id}}?trace={{trace_id}}"
			max_send_iterations 3

			request resource_id = "42"
			runtime trace_id = guid()

			request.SetHeader("X-Shell-Surface", "editor-first"
			console.Log("Prepared request before send."
			variables.Set("request_name", "Users Feed"

			expect status == 200 "returns 200"
			""";

		string normalized = RequestWorkbenchDocumentNormalizer.NormalizeRequestDocumentSource(source, string.Empty, "Users Feed");

		StringAssert.Contains(normalized, "request.headers[\"X-Shell-Surface\"] = \"editor-first\"");
		StringAssert.Contains(normalized, "log \"Prepared request before send.\"");
		StringAssert.Contains(normalized, "runtime request_name = \"Users Feed\"");
		Assert.IsFalse(normalized.Contains("request.SetHeader(", StringComparison.Ordinal));
		Assert.IsFalse(normalized.Contains("console.Log(", StringComparison.Ordinal));
		Assert.IsFalse(normalized.Contains("variables.Set(", StringComparison.Ordinal));
	}

	[TestMethod]
	public void NormalizeRequestDocumentSource_repairs_extra_closing_parentheses_on_legacy_helper_calls()
	{
		string source =
			"""
			name "Users Feed"
			method GET
			url "https://httpbin.org/anything/requests/users/list/{{resource_id}}?trace={{trace_id}}"

			request.SetHeader("X-Shell-Surface", "editor-first"))
			console.Log("Prepared request before send."))
			""";

		string normalized = RequestWorkbenchDocumentNormalizer.NormalizeRequestDocumentSource(source, string.Empty, "Users Feed");

		StringAssert.Contains(normalized, "request.headers[\"X-Shell-Surface\"] = \"editor-first\"");
		StringAssert.Contains(normalized, "log \"Prepared request before send.\"");
		Assert.IsFalse(normalized.Contains("))", StringComparison.Ordinal));
	}

	[TestMethod]
	public void NormalizeRequestDocumentSource_preserves_comparison_operators_inside_flow_assertions()
	{
		string source =
			"""
			name "restful-api QA"
			method GET
			url "https://api.restful-api.dev/objects"

			let sent = request.send()
			tests.Assert(sent.status >= 200 and sent.status < 300, "list returns 2xx")
			if sent.status == 200 and not (sent.body.length() == 0) {
			  log $"Attempt returned {sent.status}."
			}
			""";

		string normalized = RequestWorkbenchDocumentNormalizer.NormalizeRequestDocumentSource(source, string.Empty, "restful-api QA");

		StringAssert.Contains(normalized, "tests.Assert(sent.status >= 200 and sent.status < 300, \"list returns 2xx\")");
		StringAssert.Contains(normalized, "if sent.status == 200 and not (sent.body.length() == 0) {");
		Assert.IsFalse(normalized.Contains("> =", StringComparison.Ordinal));
		Assert.IsFalse(normalized.Contains("= =", StringComparison.Ordinal));
	}
}
