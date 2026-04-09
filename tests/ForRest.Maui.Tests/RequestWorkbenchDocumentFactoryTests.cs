using System;
using System.Linq;
using ForRest.Maui.Services;
using ForRest.Scripting;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class RequestWorkbenchDocumentFactoryTests
{
	[TestMethod]
	public void CreateNewRequest_builds_unique_request_for_selected_workspace()
	{
		RequestWorkbenchWorkspaceState workspace = new()
		{
			Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Name = "Echo Lab",
			SelectedEnvironment = "Local",
			SelectedDocumentLocation = "/requests/echo-lab/get-headers",
			Documents =
			[
				new()
				{
					Title = "New Request",
					Method = "GET",
					Summary = "Existing request",
					Location = "/requests/echo-lab/new-request",
					RequestSource = "request {}",
					PreRequestScript = string.Empty
				},
				new()
				{
					Title = "New Request 2",
					Method = "GET",
					Summary = "Existing request 2",
					Location = "/requests/echo-lab/new-request-2",
					RequestSource = "request {}",
					PreRequestScript = string.Empty
				}
			]
		};

		RequestWorkbenchDocumentState created = RequestWorkbenchDocumentFactory.CreateNewRequest(
			workspace,
			location => $"https://httpbin.org/anything?source={Uri.EscapeDataString(location)}");

		Assert.AreEqual("New Request 3", created.Title);
		Assert.AreEqual("GET", created.Method);
		Assert.AreEqual("/requests/echo-lab/new-request-3", created.Location);
		StringAssert.Contains(created.RequestSource, "name \"New Request 3\"");
		StringAssert.Contains(created.RequestSource, "method GET");
		StringAssert.Contains(created.RequestSource, "max_send_iterations 5");
		StringAssert.Contains(created.RequestSource, "https://httpbin.org/anything?source=%2Frequests%2Fecho-lab%2Fnew-request-3");
		StringAssert.Contains(created.RequestSource, "runtime trace_id = guid()");
		StringAssert.Contains(created.RequestSource, "runtime started_at = now()");
		StringAssert.Contains(created.RequestSource, "on error {");
		StringAssert.Contains(created.RequestSource, "on status 429 {");
		StringAssert.Contains(created.RequestSource, "retry 2 with backoff {");
		StringAssert.Contains(created.RequestSource, "retry 3 with backoff {");
		StringAssert.Contains(created.RequestSource, "let sent = request.send() as \"primary\"");
		StringAssert.Contains(created.RequestSource, "stash.Status = response.status");
		StringAssert.Contains(created.RequestSource, "stash.Body = strings.Substring(response.body, 0, 80)");
		StringAssert.Contains(created.RequestSource, "stash.Commit()");
		Assert.AreEqual(string.Empty, created.PreRequestScript);
	}

	[TestMethod]
	public void CreateNewRequest_builds_starter_script_and_tests()
	{
		RequestWorkbenchWorkspaceState workspace = new()
		{
			Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			Name = "JSON Placeholder",
			Documents = []
		};

		RequestWorkbenchDocumentState created = RequestWorkbenchDocumentFactory.CreateNewRequest(
			workspace,
			_ => "https://jsonplaceholder.typicode.com/posts/1");

		Assert.AreEqual("New request ready to edit and send", created.Summary);
		StringAssert.Contains(created.RequestSource, "expect header \"Content-Type\" contains \"json\" \"json response\"");
		StringAssert.Contains(created.RequestSource, "expect status == 200 \"returns 200\"");
		StringAssert.Contains(created.RequestSource, "method GET");
		StringAssert.Contains(created.RequestSource, "# Declarative error and status handlers run after the main flow.");
		StringAssert.Contains(created.RequestSource, "on error {");
		StringAssert.Contains(created.RequestSource, "on status 429 {");
		StringAssert.Contains(created.RequestSource, "retry 2 with backoff {");
		StringAssert.Contains(created.RequestSource, "# Send with automatic retry on transient failures.");
		StringAssert.Contains(created.RequestSource, "retry 3 with backoff {");
		StringAssert.Contains(created.RequestSource, "let sent = request.send() as \"primary\"");
		StringAssert.Contains(created.RequestSource, "if response.status == 200 {");
		StringAssert.Contains(created.RequestSource, "stash.Commit()");
		Assert.AreEqual(string.Empty, created.PreRequestScript);
	}

	[TestMethod]
	public void CreateNewRequest_starter_script_compiles_without_editor_errors()
	{
		RequestWorkbenchWorkspaceState workspace = new()
		{
			Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
			Name = "Echo Lab",
			Documents = []
		};

		RequestWorkbenchDocumentState created = RequestWorkbenchDocumentFactory.CreateNewRequest(
			workspace,
			location => $"https://httpbin.org/anything?source={Uri.EscapeDataString(location)}");
		ForRestScriptCompiler compiler = new(new ForRestScriptParser());
		ForRestScriptCompilationResult result = compiler.Compile(
			created.RequestSource,
			new ForRestScriptCompilationOptions
			{
				WorkspaceId = workspace.Id,
				DefaultRequestName = created.Title
			});

		Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
		Assert.IsNotNull(result.Payload);
	}
}
