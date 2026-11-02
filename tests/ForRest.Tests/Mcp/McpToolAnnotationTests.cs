namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class McpToolAnnotationTests
{
    #region Public Methods

    [TestMethod]
    public void Every_exported_tool_has_an_explicit_behavior_classification()
    {
        var methods = typeof(ForRestMcpTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.DeclaringType == typeof(ForRestMcpTools));

        foreach (var method in methods)
        {
            Assert.IsNotNull(method.GetCustomAttribute<McpServerToolAttribute>(), method.Name);
        }
    }

    [TestMethod]
    public async Task Tool_discovery_distinguishes_inspection_from_edits_and_external_actions()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var catalog = new ForRestAiKnowledgeCatalog();
        var tools = new ForRestMcpTools(catalog, new AiDocumentationSearchService(catalog.GetDocuments()), () => null);
        await using var server = new ForRestMcpServer(new() { Enabled = true, Port = port }, tools);
        await server.StartAsync(deadline.Token);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = new($"http://127.0.0.1:{port}/"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }), cancellationToken: deadline.Token);

        var discovered = await client.ListToolsAsync(cancellationToken: deadline.Token);
        (string Name, bool ReadOnly, bool Destructive, bool Idempotent, bool OpenWorld)[] expected =
        [
            ("get_run_response", true, false, true, false),
            ("compile_script", true, false, true, false),
            ("create_workspace", false, false, false, false),
            ("replace_active_request", false, true, false, false),
            ("execute_script", false, true, false, true),
            ("browser_eval", false, true, false, true),
            ("browser_snapshot", true, false, true, true),
            ("show_in_app", false, false, true, true),
        ];

        foreach (var item in expected)
        {
            var tool = discovered.Single(tool => tool.Name == item.Name).ProtocolTool;
            Assert.IsNotNull(tool.Annotations, item.Name);
            Assert.AreEqual(item.ReadOnly, tool.Annotations.ReadOnlyHint, item.Name);
            Assert.AreEqual(item.Destructive, tool.Annotations.DestructiveHint, item.Name);
            Assert.AreEqual(item.Idempotent, tool.Annotations.IdempotentHint, item.Name);
            Assert.AreEqual(item.OpenWorld, tool.Annotations.OpenWorldHint, item.Name);
        }
    }

    #endregion
}
