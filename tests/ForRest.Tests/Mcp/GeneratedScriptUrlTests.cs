namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class GeneratedScriptUrlTests
{
    #region Public Methods

    [TestMethod]
    [DataRow(false, "https://target.test/items", "https://target.test/items?q=", "")]
    [DataRow(true, "https://target.test/items", "https://target.test/items?q=", "")]
    [DataRow(false, "https://target.test/items?x=1#section", "https://target.test/items?x=1&q=", "#section")]
    [DataRow(true, "https://target.test/items?x=1#section", "https://target.test/items?x=1&q=", "#section")]
    [DataRow(false, "https://target.test/items#section?question", "https://target.test/items?q=", "#section?question")]
    [DataRow(true, "https://target.test/items#section?question", "https://target.test/items?q=", "#section?question")]
    [DataRow(false, "https://target.test/items?", "https://target.test/items?q=", "")]
    [DataRow(true, "https://target.test/items?", "https://target.test/items?q=", "")]
    [DataRow(false, "https://target.test/items?x=1&", "https://target.test/items?x=1&q=", "")]
    [DataRow(true, "https://target.test/items?x=1&", "https://target.test/items?x=1&q=", "")]
    public async Task Generated_query_scripts_preserve_URL_components(
        bool fuzz, string target, string prefix, string suffix)
    {
        var sent = await Execute(fuzz, target, "query");

        var expected = new PayloadsApi().Category("sqli")
            .Select(payload => new Uri(prefix + Uri.EscapeDataString(payload) + suffix).AbsoluteUri)
            .Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(expected, sent.Select(request => request.Uri.AbsoluteUri).Order(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Generated_path_scripts_insert_payloads_before_queries_and_fragments(bool fuzz)
    {
        var sent = await Execute(fuzz, "https://target.test/items/?x=1#section", "url_path");

        var expected = new PayloadsApi().Category("sqli")
            .Select(payload => new Uri("https://target.test/items/" + Uri.EscapeDataString(payload) + "?x=1#section").AbsoluteUri)
            .Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(expected, sent.Select(request => request.Uri.AbsoluteUri).Order(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Query_parameter_names_are_encoded_as_a_single_key(bool fuzz)
    {
        var sent = await Execute(fuzz, "https://target.test/items", "query", "search&admin=true");

        Assert.IsNotEmpty(sent);
        Assert.IsTrue(sent.All(request => request.Uri.Query.StartsWith("?search%26admin%3Dtrue=", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Literal_braces_in_target_URLs_are_not_interpreted_as_script_expressions(bool fuzz)
    {
        var sent = await Execute(fuzz, "https://target.test/{literal}", "query");

        Assert.IsNotEmpty(sent);
        Assert.IsTrue(sent.All(request => request.Uri.AbsolutePath == "/%7Bliteral%7D"));
    }

    [TestMethod]
    [DataRow(false, "body")]
    [DataRow(true, "body")]
    [DataRow(false, "header")]
    [DataRow(true, "header")]
    public async Task Generated_callbacks_send_each_payload_in_the_selected_location(bool fuzz, string mode)
    {
        var sent = await Execute(fuzz, "https://target.test/items", mode);
        var values = mode == "body"
            ? sent.Select(request => JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body.RawContent)!["value"])
            : sent.Select(request => request.Headers.Single(header => header.Key == "X-Fuzz").Value);

        CollectionAssert.AreEqual(new PayloadsApi().Category("sqli").Order(StringComparer.Ordinal).ToArray(),
            values.Order(StringComparer.Ordinal).ToArray());
        if (mode == "body")
        {
            Assert.IsTrue(sent.All(request => request.Method == HttpMethodKind.Post));
            Assert.IsTrue(sent.All(request => request.Body.ContentType == "application/json"));
        }
    }

    [TestMethod]
    [DataRow("query")]
    [DataRow("url_path")]
    [DataRow("body")]
    [DataRow("header")]
    public async Task Concurrent_fuzz_attempts_preserve_the_original_request(string mode)
    {
        const string target = "https://target.test/items";
        var catalog = new ForRestAiKnowledgeCatalog();
        var tools = new ForRestMcpTools(catalog, new AiDocumentationSearchService(catalog.GetDocuments()), () => null);
        var source = tools.build_fuzz_script(target, "sqli", mode, max_concurrency: 8, delay_ms: 1);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
        Assert.IsTrue(compilation.Succeeded);
        var original = new PreparedRequest
        {
            Uri = new(target),
            Method = compilation.Payload!.Request.Method,
            Body = new() { Mode = RequestBodyMode.RawText, RawContent = "fixture-original-body", ContentType = "text/plain" },
            Headers = [new() { Key = "X-Keep", Value = "fixture-original-header" }],
        };
        List<PreparedRequest> sent = [];
        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);

        var result = await engine.Run(new()
        {
            Script = compilation.Payload.Request.PreRequestScript,
            PreparedRequest = original,
            MaxSendIterations = compilation.Payload.Request.MaxSendIterations,
            SendAsync = async request =>
            {
                lock (sent)
                {
                    sent.Add(request);
                }
                await Task.Delay(2);
                return new() { StatusCode = 200, Body = "ok" };
            },
        });

        Assert.AreEqual(string.Empty, result.ErrorMessage, result.ErrorMessage);
        Assert.AreEqual(original.Uri, result.PreparedRequest.Uri);
        Assert.AreEqual(original.Body, result.PreparedRequest.Body);
        CollectionAssert.AreEqual(original.Headers.Select(item => (item.Key, item.Value, item.IsEnabled)).ToArray(),
            result.PreparedRequest.Headers.Select(item => (item.Key, item.Value, item.IsEnabled)).ToArray());
        var payloads = new PayloadsApi().Category("sqli");
        Assert.HasCount(payloads.Count, sent);
        Assert.AreEqual(payloads.Count, result.SendCount);
        Assert.HasCount(payloads.Count, result.SentRequests);
        Assert.HasCount(payloads.Count, result.SentResponses);
        var actualValues = sent.Select(request => mode switch
        {
            "body" => JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body.RawContent)!["value"],
            "header" => request.Headers.Single(item => item.Key == "X-Fuzz").Value,
            "url_path" => Uri.UnescapeDataString(request.Uri.AbsolutePath["/items/".Length..]),
            _ => Uri.UnescapeDataString(request.Uri.Query["?q=".Length..]),
        });
        CollectionAssert.AreEqual(payloads.Order(StringComparer.Ordinal).ToArray(),
            actualValues.Order(StringComparer.Ordinal).ToArray());
    }

    #endregion

    #region Private Methods

    private static async Task<List<PreparedRequest>> Execute(bool fuzz, string target, string mode, string parameter = "")
    {
        var catalog = new ForRestAiKnowledgeCatalog();
        var tools = new ForRestMcpTools(catalog, new AiDocumentationSearchService(catalog.GetDocuments()), () => null);
        var source = fuzz
            ? tools.build_fuzz_script(target, "sqli", mode, parameter, max_concurrency: 1, delay_ms: 0)
            : tools.build_attack_script(target, "sqli", mode, parameter: parameter);
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
        Assert.IsTrue(compilation.Succeeded, string.Join('\n', compilation.Diagnostics.Select(item => item.Message)));
        Assert.IsNotNull(compilation.Payload);
        List<PreparedRequest> sent = [];
        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);

        var result = await engine.Run(new()
        {
            Script = compilation.Payload.Request.PreRequestScript,
            PreparedRequest = new()
            {
                Uri = new(compilation.Payload.Request.UrlTemplate),
                Method = compilation.Payload.Request.Method,
                Body = compilation.Payload.Request.Body,
                Headers = compilation.Payload.Request.Headers,
            },
            MaxSendIterations = compilation.Payload.Request.MaxSendIterations,
            SendAsync = async request =>
            {
                sent.Add(request);
                await Task.Yield();
                return new() { StatusCode = 200, Body = "ok" };
            },
        });

        Assert.AreEqual(string.Empty, result.ErrorMessage, result.ErrorMessage);
        Assert.HasCount(new PayloadsApi().Category("sqli").Count, sent);
        return sent;
    }

    #endregion
}
