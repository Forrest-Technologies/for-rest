using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ForRest.Domain;
using ForRest.Maui.Services;
using ForRest.Models;
using ForRest.Services;
using ForRest.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class MobileRequestExecutionServiceTests
{
	[TestMethod]
	public async Task Execute_runs_full_scripting_flow_for_mobile_workbench_requests()
	{
		using LocalHttpServer server = new("""{"uuid":"alpha"}""");
		InMemoryExecutionHistoryRepository historyRepository = new();
		RequestExecutionService service = new(
			new RequestCompiler(new VariableResolver()),
			new ResponseExtractionService(),
			historyRepository,
			new RepeatRunnerService(),
			new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance),
			NullLogger<RequestExecutionService>.Instance,
			new RequestAuthenticationService(NullLogger<RequestAuthenticationService>.Instance));
		WorkspaceSnapshot workspace = new()
		{
			Workspace = new WorkspaceDefinition
			{
				Id = Guid.NewGuid(),
				Name = "Mobile Workspace",
			},
		};
		RequestDefinition request = new()
		{
			WorkspaceId = workspace.Workspace.Id,
			Name = "Local Request",
			Method = HttpMethodKind.Get,
			UrlTemplate = server.Url,
			MaxSendIterations = 1,
			SaveResponseToHistory = true,
			PreRequestScript =
			"""
			var sent = await request.send();
			variables.Set("captured_uuid", sent.uuid);
			stash.Status = sent.status;
			stash.Uuid = sent.uuid;
			stash.Commit();
			""",
			TestsScript =
			"""
			tests.Equal(200, response.Status, "status is 200");
			tests.Equal("alpha", response.uuid, "uuid is exposed");
			""",
		};

		RequestExecutionResult result = await service.Execute(new AppProfile(), workspace, request, environment: null);
		List<ExecutionRun> history = await historyRepository.Load(workspace.Workspace.Id);

		Assert.AreEqual(ExecutionState.Completed, result.State);
		Assert.IsNotNull(result.LatestResponse);
		Assert.AreEqual(200, result.LatestResponse.StatusCode);
		Assert.AreEqual("alpha", result.RuntimeVariables.Single(static item => item.Key == "captured_uuid").Value);
		Assert.HasCount(2, result.Tests);
		Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
		CollectionAssert.AreEqual(new[] { "Status", "Uuid" }, result.Stash.Columns.ToArray());
		Assert.HasCount(1, result.Stash.Rows);
		Assert.AreEqual("200", result.Stash.Rows[0].Values["Status"]);
		Assert.AreEqual("alpha", result.Stash.Rows[0].Values["Uuid"]);
		Assert.HasCount(1, history);
		Assert.IsFalse(result.ConsoleEntries.Any(static entry => entry.Message.Contains("mobile-safe mode", StringComparison.OrdinalIgnoreCase)));
	}

	private sealed class LocalHttpServer : IDisposable
	{
		private readonly TcpListener _listener;
		private readonly Task _serverTask;

		public LocalHttpServer(string responseBody)
		{
			_listener = new TcpListener(IPAddress.Loopback, 0);
			_listener.Start();
			int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
			Url = $"http://127.0.0.1:{port}/";
			_serverTask = Task.Run(async () =>
			{
				try
				{
					using TcpClient client = await _listener.AcceptTcpClientAsync();
					using NetworkStream stream = client.GetStream();
					byte[] requestBuffer = new byte[2048];
					_ = await stream.ReadAsync(requestBuffer);

					byte[] payload = Encoding.UTF8.GetBytes(responseBody);
					string headers =
						$"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
					byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
					await stream.WriteAsync(headerBytes);
					await stream.WriteAsync(payload);
					await stream.FlushAsync();
				}
				catch
				{
				}
			});
		}

		public string Url { get; }

		public void Dispose()
		{
			_listener.Stop();
			try
			{
				_serverTask.Wait(TimeSpan.FromSeconds(2));
			}
			catch
			{
			}
		}
	}
}
