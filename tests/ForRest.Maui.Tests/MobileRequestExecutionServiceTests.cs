using System.Net;
using System.Net.Sockets;
using System.Text;
using ForRest.Domain;
using ForRest.Maui.Services;
using ForRest.Models;
using ForRest.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class MobileRequestExecutionServiceTests
{
	[TestMethod]
	public async Task Execute_sends_request_and_records_mobile_safe_warning()
	{
		using LocalHttpServer server = new("""{"ok":true}""");
		InMemoryExecutionHistoryRepository historyRepository = new();
		MobileRequestExecutionService service = new(
			new RequestCompiler(new VariableResolver()),
			new ResponseExtractionService(),
			historyRepository,
			new RepeatRunnerService(),
			NullLogger<MobileRequestExecutionService>.Instance);
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
			PreRequestScript = "await request.send();",
			TestsScript = "tests.Assert(true, \"noop\");",
		};

		RequestExecutionResult result = await service.Execute(new AppProfile(), workspace, request, environment: null);
		List<ExecutionRun> history = await historyRepository.Load(workspace.Workspace.Id);

		Assert.AreEqual(ExecutionState.Completed, result.State);
		Assert.IsNotNull(result.LatestResponse);
		Assert.AreEqual(200, result.LatestResponse.StatusCode);
		Assert.HasCount(1, history);
		Assert.HasCount(1, result.ConsoleEntries);
		StringAssert.Contains(result.ConsoleEntries[0].Message, "mobile-safe mode");
		Assert.IsEmpty(result.Tests);
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
