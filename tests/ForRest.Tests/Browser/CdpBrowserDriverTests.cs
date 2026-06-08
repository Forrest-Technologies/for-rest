using System.Linq;
using System.Text.Json.Nodes;
using ForRest.Browser;

namespace ForRest.Tests.Browser;

[TestClass]
public sealed class CdpBrowserDriverTests
{
    [TestMethod]
    public async Task Navigate_posts_page_navigate_and_waits_for_load()
    {
        FakeCdpTransport transport = new();
        CdpBrowserDriver driver = new(new CdpClient(transport));

        // FakeCdpTransport raises Page.frameStoppedLoading on navigate, so this completes promptly.
        await driver.Navigate("https://example.test/");

        (string method, string parameters) = transport.Calls.Single(call => call.Method == "Page.navigate");
        Assert.AreEqual("Page.navigate", method);
        StringAssert.Contains(parameters, "https://example.test/");
    }

    [TestMethod]
    public async Task Click_locates_then_moves_and_presses()
    {
        FakeCdpTransport transport = new()
        {
            Responder = (method, _) => method == "Runtime.evaluate"
                ? FakeCdpTransport.LocateResult(50, 30)
                : "{}",
        };
        CdpBrowserDriver driver = new(new CdpClient(transport));

        await driver.Click(BrowserTarget.Css("#target"), CursorMotion.Instant);

        // One evaluate to locate, at least one mouseMoved, then a press/release pair at the element centre.
        Assert.IsTrue(transport.Calls.Any(call => call.Method == "Runtime.evaluate"));
        var mouse = transport.Calls.Where(call => call.Method == "Input.dispatchMouseEvent").ToList();
        Assert.IsTrue(mouse.Any(call => Type(call.Parameters) == "mouseMoved"));
        Assert.IsTrue(mouse.Any(call => Type(call.Parameters) == "mousePressed"));
        Assert.IsTrue(mouse.Any(call => Type(call.Parameters) == "mouseReleased"));

        (string _, string pressed) = mouse.First(call => Type(call.Parameters) == "mousePressed");
        Assert.AreEqual(50, JsonNode.Parse(pressed)!["x"]!.GetValue<double>());
    }

    [TestMethod]
    public async Task Click_with_visible_motion_drives_the_red_cursor_overlay()
    {
        FakeCdpTransport transport = new()
        {
            Responder = (method, _) => method == "Runtime.evaluate"
                ? FakeCdpTransport.LocateResult(50, 30)
                : "{}",
        };
        CdpBrowserDriver driver = new(new CdpClient(transport));

        // CursorMotion.Default is visible, so the engine should animate the red cursor to the target.
        await driver.Click(BrowserTarget.Css("#target"), CursorMotion.Default);

        Assert.IsTrue(
            transport.Calls.Any(call => call.Method == "Runtime.evaluate" && call.Parameters.Contains("__forrest_cursor__")),
            "expected the visible cursor overlay to be driven during a click");
    }

    [TestMethod]
    public async Task Click_with_instant_motion_does_not_draw_the_cursor_overlay()
    {
        FakeCdpTransport transport = new()
        {
            Responder = (method, _) => method == "Runtime.evaluate"
                ? FakeCdpTransport.LocateResult(50, 30)
                : "{}",
        };
        CdpBrowserDriver driver = new(new CdpClient(transport));

        await driver.Click(BrowserTarget.Css("#target"), CursorMotion.Instant);

        Assert.IsFalse(
            transport.Calls.Any(call => call.Method == "Runtime.evaluate" && call.Parameters.Contains("__forrest_cursor__")),
            "an invisible (instant) motion must not paint the cursor overlay");
    }

    [TestMethod]
    public async Task Type_clicks_then_inserts_text()
    {
        FakeCdpTransport transport = new()
        {
            Responder = (method, _) => method == "Runtime.evaluate"
                ? FakeCdpTransport.LocateResult(10, 10)
                : "{}",
        };
        CdpBrowserDriver driver = new(new CdpClient(transport));

        await driver.Type(BrowserTarget.Css("#name"), "hello", CursorMotion.Instant);

        (string method, string parameters) = transport.Calls.Single(call => call.Method == "Input.insertText");
        Assert.AreEqual("Input.insertText", method);
        Assert.AreEqual("hello", JsonNode.Parse(parameters)!["text"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Press_named_key_dispatches_key_events()
    {
        FakeCdpTransport transport = new();
        CdpBrowserDriver driver = new(new CdpClient(transport));

        await driver.Press("Enter");

        var keyEvents = transport.Calls.Where(call => call.Method == "Input.dispatchKeyEvent").ToList();
        Assert.AreEqual(2, keyEvents.Count);
        Assert.AreEqual("Enter", JsonNode.Parse(keyEvents[0].Parameters)!["key"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task GetText_returns_operate_value()
    {
        FakeCdpTransport transport = new()
        {
            Responder = (_, _) => FakeCdpTransport.EvaluateResult("{\"ok\":true,\"value\":\"Submit\"}"),
        };
        CdpBrowserDriver driver = new(new CdpClient(transport));

        string text = await driver.GetText(BrowserTarget.Css("#btn"));

        Assert.AreEqual("Submit", text);
    }

    [TestMethod]
    public async Task Click_throws_when_element_missing()
    {
        FakeCdpTransport transport = new()
        {
            Responder = (_, _) => FakeCdpTransport.EvaluateResult("{\"found\":false}"),
        };
        CdpBrowserDriver driver = new(new CdpClient(transport));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => driver.Click(BrowserTarget.Css("#missing"), CursorMotion.Instant));
    }

    [TestMethod]
    public async Task WaitFor_returns_when_found_immediately()
    {
        FakeCdpTransport transport = new()
        {
            Responder = (_, _) => FakeCdpTransport.LocateResult(5, 5),
        };
        CdpBrowserDriver driver = new(new CdpClient(transport));

        BrowserElementInfo info = await driver.WaitFor(BrowserTarget.Css("#ready"), 1000);

        Assert.IsTrue(info.Found);
    }

    private static string Type(string parametersJson) => JsonNode.Parse(parametersJson)!["type"]!.GetValue<string>();
}
