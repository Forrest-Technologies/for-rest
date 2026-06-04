using System.Net.Sockets;

namespace ForRest.Services.AI.OAuth;

/// <summary>
/// Binds an <see cref="HttpListener"/> on <c>http://127.0.0.1:&lt;free port&gt;/</c> and awaits a single
/// OAuth redirect, returning the captured code (after validating <c>state</c>). The socket-free parts
/// (port selection, URL composition, request parsing) are exposed as static helpers for unit testing.
/// </summary>
public sealed class LoopbackRedirectListener : IDisposable
{
    #region Private Fields

    private readonly HttpListener listener = new();

    private readonly string redirectUri;

    private readonly string expectedState;

    #endregion

    #region Constructors

    public LoopbackRedirectListener(int port, string redirectPath, string expectedState)
    {
        string prefixPath = NormalizePath(redirectPath);
        this.expectedState = expectedState;
        redirectUri = ComposeRedirectUri(port, redirectPath);

        // Listen at the path root so trailing query strings still match.
        listener.Prefixes.Add($"http://127.0.0.1:{port}{prefixPath}");
        listener.Prefixes.Add($"http://localhost:{port}{prefixPath}");
    }

    #endregion

    #region Properties

    public string RedirectUri => redirectUri;

    #endregion

    #region Public Methods

    /// <summary>Reserves a free loopback TCP port by binding to port 0 and reading the assigned port.</summary>
    public static int GetFreeLoopbackPort()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    public static string ComposeRedirectUri(int port, string redirectPath)
    {
        return $"http://127.0.0.1:{port}{NormalizePath(redirectPath)}";
    }

    public void Start() => listener.Start();

    /// <summary>
    /// Awaits a single inbound redirect, writes a small confirmation page, and returns the parsed,
    /// state-validated result.
    /// </summary>
    public async Task<OAuthRedirectResult> CaptureCode(CancellationToken cancellationToken = default)
    {
        if (!listener.IsListening)
        {
            listener.Start();
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(static state => ((HttpListener)state!).Abort(), listener);

        HttpListenerContext context = await listener.GetContextAsync();
        try
        {
            OAuthRedirectResult result = OAuthRedirectParser.ParseAndValidate(
                context.Request.Url?.ToString() ?? string.Empty,
                expectedState);

            await WriteResponse(context.Response, "Sign-in complete. You can close this window and return to For-Rest.");
            return result;
        }
        catch (OAuthAuthorizationException ex)
        {
            await WriteResponse(context.Response, $"Sign-in failed: {ex.Message}", statusCode: 400);
            throw;
        }
    }

    public void Dispose()
    {
        if (listener.IsListening)
        {
            listener.Stop();
        }

        listener.Close();
    }

    #endregion

    #region Private Methods

    private static string NormalizePath(string redirectPath)
    {
        if (string.IsNullOrWhiteSpace(redirectPath))
        {
            return "/";
        }

        string path = redirectPath.StartsWith('/') ? redirectPath : "/" + redirectPath;
        return path.EndsWith('/') ? path : path + "/";
    }

    private static async Task WriteResponse(HttpListenerResponse response, string message, int statusCode = 200)
    {
        byte[] payload = Encoding.UTF8.GetBytes($"<html><body><p>{WebUtility.HtmlEncode(message)}</p></body></html>");
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload);
        response.OutputStream.Close();
    }

    #endregion
}
