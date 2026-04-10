using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ForRest.Mcp;

/// <summary>
/// Minimal TCP-hosted MCP server. Each accepted connection gets its own
/// <see cref="StreamServerTransport"/> and <see cref="McpServer"/>, so
/// multiple external agents can talk to For-Rest simultaneously (up to
/// <see cref="ForRestMcpServerSettings.MaxConcurrentSessions"/>).
///
/// The server is intentionally transport-agnostic wrt its tool set —
/// tools are built once at construction time from <see cref="ForRestMcpTools"/>
/// and reused on every session.
/// </summary>
public sealed class ForRestMcpServer : IAsyncDisposable
{
    #region Private Fields

    private readonly ForRestMcpServerSettings settings;
    private readonly ForRestMcpTools tools;
    private readonly ILogger<ForRestMcpServer> logger;
    private readonly ILoggerFactory loggerFactory;
    private readonly SemaphoreSlim sessionGate;
    private readonly List<Task> activeSessions = [];
    private readonly object activeSessionsLock = new();
    private TcpListener? listener;
    private CancellationTokenSource? shutdown;
    private Task? acceptLoop;

    #endregion

    #region Constructors

    public ForRestMcpServer(
        ForRestMcpServerSettings settings,
        ForRestMcpTools tools,
        ILoggerFactory? loggerFactory = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.tools = tools ?? throw new ArgumentNullException(nameof(tools));
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        logger = this.loggerFactory.CreateLogger<ForRestMcpServer>();
        sessionGate = new SemaphoreSlim(Math.Max(1, settings.MaxConcurrentSessions));
    }

    #endregion

    #region Public API

    public bool IsRunning => listener is not null;

    public IPEndPoint? Endpoint => listener?.LocalEndpoint as IPEndPoint;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (listener is not null)
        {
            return Task.CompletedTask;
        }

        if (!settings.Enabled)
        {
            logger.LogInformation("ForRest MCP server is disabled in settings; not starting.");
            return Task.CompletedTask;
        }

        IPAddress address = IPAddress.TryParse(settings.BindAddress, out IPAddress? parsed)
            ? parsed
            : IPAddress.Loopback;

        listener = new TcpListener(address, settings.Port);
        listener.Start();
        shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        logger.LogInformation("ForRest MCP server listening on {Endpoint}", listener.LocalEndpoint);
        acceptLoop = Task.Run(() => AcceptLoopAsync(shutdown.Token), shutdown.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (listener is null)
        {
            return;
        }

        try
        {
            shutdown?.Cancel();
            listener.Stop();
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }

        if (acceptLoop is not null)
        {
            try
            {
                await acceptLoop.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("ForRest MCP accept loop did not terminate within 2 seconds.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        Task[] pending;
        lock (activeSessionsLock)
        {
            pending = [.. activeSessions];
            activeSessions.Clear();
        }

        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("ForRest MCP sessions did not drain within 2 seconds.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        listener = null;
        acceptLoop = null;
        shutdown?.Dispose();
        shutdown = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        sessionGate.Dispose();
    }

    #endregion

    #region Accept loop

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        if (listener is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "ForRest MCP accept loop failed; will retry.");
                continue;
            }

            Task sessionTask = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            lock (activeSessionsLock)
            {
                activeSessions.Add(sessionTask);
                activeSessions.RemoveAll(task => task.IsCompleted);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        if (!await sessionGate.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken))
        {
            logger.LogWarning("Rejecting MCP connection from {Endpoint}: max concurrent sessions reached.", client.Client?.RemoteEndPoint);
            client.Close();
            return;
        }

        try
        {
            using NetworkStream stream = client.GetStream();
            if (settings.HasAuthToken && !await ValidateAuthTokenAsync(stream, cancellationToken))
            {
                logger.LogWarning("Rejecting MCP connection from {Endpoint}: invalid auth token.", client.Client?.RemoteEndPoint);
                return;
            }

            StreamServerTransport transport = new(stream, stream, "forrest-mcp", loggerFactory);
            McpServerOptions options = BuildServerOptions();
            await using McpServer server = McpServer.Create(transport, options, loggerFactory, serviceProvider: null);
            await server.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException ioException)
        {
            logger.LogDebug(ioException, "ForRest MCP session ended with IO error.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "ForRest MCP session failed.");
        }
        finally
        {
            client.Dispose();
            sessionGate.Release();
        }
    }

    private async Task<bool> ValidateAuthTokenAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // Simple `forrest-mcp-auth: <token>\n` preamble. This is *not* the
        // MCP protocol — it's a lightweight pre-handshake gate for when the
        // server is exposed beyond 127.0.0.1.
        byte[] buffer = new byte[256];
        int read = 0;
        while (read < buffer.Length)
        {
            int step = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
            if (step <= 0)
            {
                return false;
            }

            read += step;
            int newlineIndex = Array.IndexOf(buffer, (byte)'\n', 0, read);
            if (newlineIndex >= 0)
            {
                string line = System.Text.Encoding.UTF8.GetString(buffer, 0, newlineIndex).TrimEnd('\r');
                const string prefix = "forrest-mcp-auth: ";
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string presented = line[prefix.Length..].Trim();
                return string.Equals(presented, settings.AuthToken, StringComparison.Ordinal);
            }
        }

        return false;
    }

    private McpServerOptions BuildServerOptions()
    {
        McpServerPrimitiveCollection<McpServerTool> collection = new(StringComparer.Ordinal);
        foreach (McpServerTool tool in BuildTools())
        {
            collection.Add(tool);
        }

        return new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "For-Rest",
                Version = typeof(ForRestMcpServer).Assembly.GetName().Version?.ToString() ?? "1.0",
            },
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability
                {
                    ListChanged = false,
                },
            },
            ToolCollection = collection,
        };
    }

    private IEnumerable<McpServerTool> BuildTools()
    {
        foreach (MethodInfo method in tools.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.DeclaringType != typeof(ForRestMcpTools))
            {
                continue;
            }

            yield return McpServerTool.Create(method, tools, new McpServerToolCreateOptions
            {
                Name = method.Name,
                Description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description ?? method.Name,
            });
        }
    }

    #endregion
}
