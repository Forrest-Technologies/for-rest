namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class McpOriginValidationTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("POST", "https://untrusted.example")]
    [DataRow("GET", "https://untrusted.example")]
    [DataRow("DELETE", "https://untrusted.example")]
    [DataRow("OPTIONS", "https://untrusted.example")]
    [DataRow("POST", "null")]
    [DataRow("POST", "http://127.0.0.1.evil.example:7341")]
    [DataRow("POST", "http://127.0.0.1:1")]
    public async Task Requests_from_untrusted_browser_origins_are_rejected(string method, string origin)
    {
        await WithServer(async endpoint =>
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(new HttpMethod(method), endpoint);
            request.Headers.Add("Origin", origin);
            if (method == "POST")
            {
                request.Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
                    Encoding.UTF8, "text/plain");
            }

            using var response = await client.SendAsync(request);

            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        });
    }

    [TestMethod]
    [DataRow("/unexpected")]
    [DataRow("?unexpected=1")]
    [DataRow("#unexpected")]
    public async Task Origin_values_must_contain_only_a_scheme_and_authority(string suffix)
    {
        await WithServer(async endpoint =>
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);
            request.Headers.Add("Origin", endpoint.GetLeftPart(UriPartial.Authority) + suffix);

            using var response = await client.SendAsync(request);

            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        });
    }

    [TestMethod]
    public async Task Native_clients_without_an_origin_and_the_configured_origin_remain_accepted()
    {
        await WithServer(async endpoint =>
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var nativeResponse = await client.DeleteAsync(endpoint);
            using var browserRequest = new HttpRequestMessage(HttpMethod.Delete, endpoint);
            browserRequest.Headers.Add("Origin", endpoint.GetLeftPart(UriPartial.Authority));
            using var browserResponse = await client.SendAsync(browserRequest);

            Assert.AreEqual(HttpStatusCode.OK, nativeResponse.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, browserResponse.StatusCode);
        });
    }

    [TestMethod]
    public async Task Multiple_origin_values_are_rejected()
    {
        await WithServer(async endpoint =>
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);
            request.Headers.TryAddWithoutValidation("Origin",
                [endpoint.GetLeftPart(UriPartial.Authority), "https://untrusted.example"]);

            using var response = await client.SendAsync(request);

            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        });
    }

    [TestMethod]
    public async Task Origin_validation_and_bearer_authentication_are_independent_requirements()
    {
        await WithServer(async endpoint =>
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var untrusted = new HttpRequestMessage(HttpMethod.Delete, endpoint);
            untrusted.Headers.Add("Origin", "https://untrusted.example");
            untrusted.Headers.Add("Authorization", "Bearer fake-test-token");
            using var untrustedResponse = await client.SendAsync(untrusted);
            using var unauthenticated = new HttpRequestMessage(HttpMethod.Delete, endpoint);
            unauthenticated.Headers.Add("Origin", endpoint.GetLeftPart(UriPartial.Authority));
            using var unauthenticatedResponse = await client.SendAsync(unauthenticated);
            using var trusted = new HttpRequestMessage(HttpMethod.Delete, endpoint);
            trusted.Headers.Add("Origin", endpoint.GetLeftPart(UriPartial.Authority));
            trusted.Headers.Add("Authorization", "Bearer fake-test-token");
            using var trustedResponse = await client.SendAsync(trusted);

            Assert.AreEqual(HttpStatusCode.Forbidden, untrustedResponse.StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, trustedResponse.StatusCode);
        }, "fake-test-token");
    }

    #endregion

    #region Private Methods

    private static async Task WithServer(Func<Uri, Task> action, string authToken = "")
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var catalog = new ForRestAiKnowledgeCatalog();
        var tools = new ForRestMcpTools(catalog, new AiDocumentationSearchService(catalog.GetDocuments()), () => null);
        await using var server = new ForRestMcpServer(new() { Enabled = true, Port = port, AuthToken = authToken }, tools);
        await server.StartAsync(timeout.Token);

        await action(new($"http://127.0.0.1:{port}/"));
    }

    #endregion
}
