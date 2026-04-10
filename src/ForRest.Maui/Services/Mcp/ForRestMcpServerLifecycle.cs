#if WINDOWS || MACCATALYST
using ForRest.Maui.Services;
using ForRest.Maui.Theming;
using ForRest.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ForRest.Maui.Services.Mcp;

/// <summary>
/// Desktop-only adapter that owns the lifetime of a <see cref="ForRestMcpServer"/>
/// based on the live `[mcp]` settings. Started from <c>App.xaml.cs</c> window
/// creation and stopped on window destroying.
/// </summary>
public sealed class ForRestMcpServerLifecycle : IAsyncDisposable
{
	private readonly IWorkbenchMcpSettingsProvider settingsProvider;
	private readonly ForRestMcpTools tools;
	private readonly ILoggerFactory loggerFactory;
	private readonly ILogger<ForRestMcpServerLifecycle> logger;
	private ForRestMcpServer? server;

	public ForRestMcpServerLifecycle(
		IWorkbenchMcpSettingsProvider settingsProvider,
		ForRestMcpTools tools,
		ILoggerFactory? loggerFactory = null)
	{
		this.settingsProvider = settingsProvider;
		this.tools = tools;
		this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
		logger = this.loggerFactory.CreateLogger<ForRestMcpServerLifecycle>();
	}

	public bool IsRunning => server?.IsRunning ?? false;

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (server is not null)
		{
			return;
		}

		ForRestMcpSettings config = settingsProvider.GetCurrentSettings();
		if (!config.Enabled)
		{
			logger.LogInformation("MCP server disabled in settings; not starting.");
			return;
		}

		ForRestMcpServerSettings runtime = new()
		{
			Enabled = config.Enabled,
			BindAddress = config.BindAddress,
			Port = config.Port,
			AuthToken = config.AuthToken,
			MaxConcurrentSessions = config.MaxConcurrentSessions,
		};

		server = new ForRestMcpServer(runtime, tools, loggerFactory);
		try
		{
			await server.StartAsync(cancellationToken);
			logger.LogInformation("ForRest MCP server started on {Endpoint}", server.Endpoint);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Failed to start ForRest MCP server.");
			await server.DisposeAsync();
			server = null;
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		if (server is null)
		{
			return;
		}

		try
		{
			await server.StopAsync(cancellationToken);
		}
		finally
		{
			await server.DisposeAsync();
			server = null;
		}
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
	}
}
#endif
