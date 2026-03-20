using System;
using ForRest.Maui.Services;

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
		StringAssert.Contains(created.RequestSource, "name = \"New Request 3\"");
		StringAssert.Contains(created.RequestSource, "method = GET");
		StringAssert.Contains(created.RequestSource, "max_send_iterations = 3");
		StringAssert.Contains(created.RequestSource, "https://httpbin.org/anything?source=%2Frequests%2Fecho-lab%2Fnew-request-3");
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
		StringAssert.Contains(created.RequestSource, "header \"Content-Type\" contains \"json\" \"json response\"");
		StringAssert.Contains(created.PreRequestScript, "variables.Set(\"request_name\", \"New Request\")");
		StringAssert.Contains(created.PreRequestScript, "request.SetHeader(\"X-Request-Source\", \"maui\")");
	}
}
