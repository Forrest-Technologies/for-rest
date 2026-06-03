using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ForRest.Mcp;

/// <summary>
/// Self-hosted MCP server speaking the <b>Streamable HTTP</b> transport (the
/// current MCP standard transport). It hosts a small <see cref="HttpListener"/>
/// and drives the SDK's <see cref="StreamableHttpServerTransport"/> in
/// <i>stateless</i> mode: every POST is an independent JSON-RPC exchange, so no
/// per-session state is retained and no server-to-client SSE stream is offered
/// (GET returns 405). For-Rest's tools are all request/response, which is the
/// scenario stateless mode is designed for.
///
/// Because the endpoint is plain Streamable HTTP, external agents connect with
/// no bridge: point them at <c>http://127.0.0.1:&lt;port&gt;/</c> directly (Claude
/// Desktop custom connector, <c>mcp-remote</c>, etc.). When an
/// <see cref="ForRestMcpServerSettings.AuthToken"/> is configured, requests must
/// carry an <c>Authorization: Bearer &lt;token&gt;</c> header.
///
/// The tool set is built once at construction from <see cref="ForRestMcpTools"/>
/// and reused for every request.
/// </summary>
public sealed class ForRestMcpServer : IAsyncDisposable
{
    #region Private Fields

    private readonly ForRestMcpServerSettings settings;
    private readonly ForRestMcpTools tools;
    private readonly ILogger<ForRestMcpServer> logger;
    private readonly ILoggerFactory loggerFactory;
    private readonly SemaphoreSlim requestGate;
    private readonly McpServerPrimitiveCollection<McpServerTool> toolCollection;
    private readonly List<Task> activeRequests = [];
    private readonly object activeRequestsLock = new();
    private HttpListener? listener;
    private CancellationTokenSource? shutdown;
    private Task? acceptLoop;
    private string? endpoint;

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
        requestGate = new SemaphoreSlim(Math.Max(1, settings.MaxConcurrentSessions));
        toolCollection = BuildToolCollection();
    }

    #endregion

    #region Public API

    public bool IsRunning => listener?.IsListening ?? false;

    /// <summary>The base URL the server is listening on, or null when stopped.</summary>
    public string? Endpoint => endpoint;

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

        // HttpListener prefixes use "+" to mean "all interfaces". Map a 0.0.0.0
        // bind onto that; everything else binds to the literal host. Binding to a
        // non-loopback host with "+" requires a urlacl/elevation on Windows.
        string prefixHost = settings.BindAddress is "0.0.0.0" or "*" or ""
            ? "+"
            : settings.BindAddress;
        string prefix = $"http://{prefixHost}:{settings.Port}/";

        // HttpListener is unavailable on some sandboxed platforms (e.g. Mac
        // Catalyst). The lifecycle adapter wraps StartAsync and degrades
        // gracefully if construction or Start throws here.
        listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        endpoint = $"http://{(prefixHost == "+" ? settings.BindAddress : prefixHost)}:{settings.Port}/";
        shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        logger.LogInformation("ForRest MCP server listening on {Endpoint} (Streamable HTTP).", endpoint);
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
            listener.Close();
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
        lock (activeRequestsLock)
        {
            pending = [.. activeRequests];
            activeRequests.Clear();
        }

        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("ForRest MCP requests did not drain within 2 seconds.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        listener = null;
        acceptLoop = null;
        endpoint = null;
        shutdown?.Dispose();
        shutdown = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        requestGate.Dispose();
    }

    #endregion

    #region Accept loop

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        HttpListener? current = listener;
        if (current is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await current.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException)
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

            Task requestTask = Task.Run(() => HandleContextAsync(context, cancellationToken), cancellationToken);
            lock (activeRequestsLock)
            {
                activeRequests.Add(requestTask);
                activeRequests.RemoveAll(task => task.IsCompleted);
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        HttpListenerResponse response = context.Response;
        if (!await requestGate.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken))
        {
            WriteStatus(response, 503, "ForRest MCP server is busy (max concurrent sessions reached).");
            return;
        }

        try
        {
            if (settings.HasAuthToken && !IsAuthorized(context.Request))
            {
                response.AddHeader("WWW-Authenticate", "Bearer");
                WriteStatus(response, 401, "Unauthorized: a valid 'Authorization: Bearer <token>' header is required.");
                return;
            }

            switch (context.Request.HttpMethod.ToUpperInvariant())
            {
                case "POST":
                    await HandlePostAsync(context, cancellationToken);
                    break;
                case "GET":
                    // Stateless Streamable HTTP: no server-initiated SSE stream is offered.
                    response.AddHeader("Allow", "POST, DELETE");
                    WriteStatus(response, 405, "This endpoint does not provide a server-initiated event stream.");
                    break;
                case "DELETE":
                    // Session termination is a no-op in stateless mode.
                    WriteStatus(response, 200, null);
                    break;
                case "OPTIONS":
                    response.AddHeader("Allow", "POST, DELETE");
                    WriteStatus(response, 204, null);
                    break;
                default:
                    response.AddHeader("Allow", "POST, DELETE");
                    WriteStatus(response, 405, "Method not allowed.");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "ForRest MCP request failed.");
            try
            {
                WriteStatus(response, 500, "Internal server error.");
            }
            catch
            {
                // Response may already be committed.
            }
        }
        finally
        {
            requestGate.Release();
            try
            {
                response.Close();
            }
            catch
            {
                // Best effort.
            }
        }
    }

    #endregion

    #region Request handling

    private async Task HandlePostAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        JsonRpcMessage? message;
        try
        {
            JsonTypeInfo<JsonRpcMessage> typeInfo =
                (JsonTypeInfo<JsonRpcMessage>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage));
            message = await JsonSerializer.DeserializeAsync(context.Request.InputStream, typeInfo, cancellationToken);
        }
        catch (JsonException exception)
        {
            logger.LogDebug(exception, "Rejected malformed MCP request body.");
            WriteStatus(context.Response, 400, "Bad request: malformed JSON-RPC message.");
            return;
        }

        if (message is null)
        {
            WriteStatus(context.Response, 400, "Bad request: empty body.");
            return;
        }

        StreamableHttpServerTransport transport = new(loggerFactory) { Stateless = true };
        McpServer server = McpServer.Create(transport, BuildServerOptions(), loggerFactory, serviceProvider: null);
        Task sessionTask = server.RunAsync(cancellationToken);
        try
        {
            using SseHttpResponseStream sseStream = new(context.Response);
            bool wroteResponse = await transport.HandlePostRequestAsync(message, sseStream, cancellationToken);
            if (!wroteResponse)
            {
                // A notification with no response payload -> acknowledge with 202.
                context.Response.StatusCode = 202;
            }
        }
        finally
        {
            await transport.DisposeAsync();
            try
            {
                await sessionTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "ForRest MCP session ended with an error.");
            }

            await server.DisposeAsync();
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        string? header = request.Headers["Authorization"];
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string presented = header[prefix.Length..].Trim();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(settings.AuthToken));
    }

    private static void WriteStatus(HttpListenerResponse response, int statusCode, string? message)
    {
        response.StatusCode = statusCode;
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        byte[] body = Encoding.UTF8.GetBytes(message);
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
    }

    #endregion

    #region Server options

    private McpServerOptions BuildServerOptions()
    {
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
            ToolCollection = toolCollection,
            // Stateless mode: requests are independent, so do not open a DI scope per request.
            ScopeRequests = false,
            // Each POST is served by a fresh server instance with no prior `initialize`
            // exchange. Pre-supplying client info lets that instance answer tools/list and
            // tools/call without the lifecycle handshake (stateless Streamable HTTP).
            KnownClientInfo = new Implementation
            {
                Name = "for-rest-mcp-client",
                Version = "1.0",
            },
        };
    }

    private McpServerPrimitiveCollection<McpServerTool> BuildToolCollection()
    {
        McpServerPrimitiveCollection<McpServerTool> collection = new(StringComparer.Ordinal);
        foreach (McpServerTool tool in BuildTools())
        {
            collection.Add(tool);
        }

        return collection;
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

    #region SSE response stream

    /// <summary>
    /// Write-through stream over an <see cref="HttpListenerResponse"/> that lazily
    /// commits Streamable HTTP / SSE response headers on the first write. If the
    /// transport produces no output (e.g. a notification), nothing is written and
    /// the caller is free to set a 202 status instead.
    /// </summary>
    private sealed class SseHttpResponseStream(HttpListenerResponse response) : Stream
    {
        private Stream? output;
        private bool initialized;

        private Stream Ensure()
        {
            if (!initialized)
            {
                initialized = true;
                response.StatusCode = 200;
                response.ContentType = "text/event-stream";
                response.Headers["Cache-Control"] = "no-cache, no-store";
                response.SendChunked = true;
                output = response.OutputStream;
            }

            return output!;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            if (initialized)
            {
                output!.Flush();
            }
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return initialized ? output!.FlushAsync(cancellationToken) : Task.CompletedTask;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Ensure().Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Ensure().WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return Ensure().WriteAsync(buffer, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && initialized)
            {
                try
                {
                    output!.Flush();
                }
                catch
                {
                    // Best effort flush on dispose.
                }
            }

            base.Dispose(disposing);
        }
    }

    #endregion
}
