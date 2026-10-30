namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class McpServerLifecycleTests
{
    #region Public Methods

    [TestMethod]
    public async Task Startup_can_be_retried_after_a_port_collision()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        await using var server = CreateServer(port);

        await Assert.ThrowsExactlyAsync<HttpListenerException>(() => server.StartAsync());
        Assert.IsFalse(server.IsRunning);
        Assert.IsNull(server.Endpoint);
        reservation.Stop();
        await server.StartAsync();

        Assert.IsTrue(server.IsRunning);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.DeleteAsync(server.Endpoint);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Pre_cancelled_startup_does_not_open_a_listener()
    {
        await using var server = CreateServer(ReservePort(IPAddress.Loopback));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => server.StartAsync(cancellation.Token));

        Assert.IsFalse(server.IsRunning);
        Assert.IsNull(server.Endpoint);
    }

    [TestMethod]
    public async Task Stopped_server_can_be_started_again()
    {
        await using var server = CreateServer(ReservePort(IPAddress.Loopback));
        await server.StartAsync();
        await server.StopAsync();
        Assert.IsFalse(server.IsRunning);
        Assert.IsNull(server.Endpoint);

        await server.StartAsync();

        Assert.IsTrue(server.IsRunning);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.DeleteAsync(server.Endpoint);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Private Methods

    private static int ReservePort(IPAddress address)
    {
        using var reservation = new TcpListener(address, 0);
        reservation.Start();
        return ((IPEndPoint)reservation.LocalEndpoint).Port;
    }

    private static ForRestMcpServer CreateServer(int port, string address = "127.0.0.1")
    {
        var catalog = new ForRestAiKnowledgeCatalog();
        var tools = new ForRestMcpTools(catalog, new AiDocumentationSearchService(catalog.GetDocuments()), () => null);
        return new(new() { Enabled = true, Port = port, BindAddress = address }, tools);
    }

    #endregion
}
