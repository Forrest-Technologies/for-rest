using System.Linq;
using System.Text.Json.Nodes;
using ForRest.Browser;

namespace ForRest.Tests.Browser;

[TestClass]
public sealed class CdpClientTests
{
    [TestMethod]
    public async Task EnableDomains_enables_required_domains()
    {
        FakeCdpTransport transport = new();
        CdpClient client = new(transport);

        await client.EnableDomains();

        CollectionAssert.AreEqual(
            new[] { "Page.enable", "DOM.enable", "Runtime.enable" },
            transport.Calls.Select(call => call.Method).ToArray());
        CollectionAssert.Contains(transport.Subscriptions, "Page.loadEventFired");
        CollectionAssert.Contains(transport.Subscriptions, "Page.frameStoppedLoading");
    }

    [TestMethod]
    public async Task InstallCursorOverlay_registers_init_script_and_installs_current_document()
    {
        FakeCdpTransport transport = new();
        CdpClient client = new(transport);

        await client.InstallCursorOverlay();

        // Registers the overlay for every future document (so it survives navigation)...
        (string initMethod, string initParameters) = transport.Calls.Single(call => call.Method == "Page.addScriptToEvaluateOnNewDocument");
        Assert.AreEqual("Page.addScriptToEvaluateOnNewDocument", initMethod);
        StringAssert.Contains(initParameters, "__forrest_cursor__");

        // ...and installs it on the document that is already loaded.
        (string evalMethod, string evalParameters) = transport.Calls.Single(call => call.Method == "Runtime.evaluate");
        Assert.AreEqual("Runtime.evaluate", evalMethod);
        StringAssert.Contains(evalParameters, "__forrest_cursor__");
    }

    [TestMethod]
    public async Task Navigate_sends_url()
    {
        FakeCdpTransport transport = new();
        CdpClient client = new(transport);

        await client.Navigate("https://example.test/");

        (string method, string parameters) = transport.Calls.Single();
        Assert.AreEqual("Page.navigate", method);
        Assert.AreEqual("https://example.test/", JsonNode.Parse(parameters)!["url"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task DispatchMouse_includes_button_state()
    {
        FakeCdpTransport transport = new();
        CdpClient client = new(transport);

        await client.DispatchMouse("mousePressed", 10, 20, "left", 1);

        JsonNode parameters = JsonNode.Parse(transport.Calls.Single().Parameters)!;
        Assert.AreEqual("mousePressed", parameters["type"]!.GetValue<string>());
        Assert.AreEqual(10, parameters["x"]!.GetValue<double>());
        Assert.AreEqual(1, parameters["buttons"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task CaptureScreenshot_returns_base64_data()
    {
        FakeCdpTransport transport = new()
        {
            Responder = (_, _) => "{\"result\":{\"data\":\"QUJD\"}}",
        };
        CdpClient client = new(transport);

        string data = await client.CaptureScreenshot();

        Assert.AreEqual("QUJD", data);
    }

    [TestMethod]
    public async Task EvaluateJson_unwraps_stringified_result()
    {
        FakeCdpTransport transport = new()
        {
            Responder = (_, _) => FakeCdpTransport.EvaluateResult("{\"hello\":\"world\"}"),
        };
        CdpClient client = new(transport);

        JsonNode? node = await client.EvaluateJson("doesNotMatter()");

        Assert.AreEqual("world", node!["hello"]!.GetValue<string>());
    }
}
