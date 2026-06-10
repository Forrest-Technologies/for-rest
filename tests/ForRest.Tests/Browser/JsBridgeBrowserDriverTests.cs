using System.Collections.Generic;
using System.Linq;
using ForRest.Browser;

namespace ForRest.Tests.Browser;

[TestClass]
public sealed class JsBridgeBrowserDriverTests
{
    [TestMethod]
    public async Task Navigate_navigates_and_installs_the_cursor_overlay()
    {
        FakeBrowserPageTransport transport = new();
        JsBridgeBrowserDriver driver = new(transport);

        await driver.Navigate("https://example.test/");

        CollectionAssert.Contains(transport.Navigations, "https://example.test/");
        Assert.IsTrue(
            transport.Scripts.Any(script => script.Contains("__forrest_cursor__")),
            "navigation should re-install the cursor overlay");
    }

    [TestMethod]
    public async Task Click_locates_moves_the_cursor_then_dispatches_a_synthetic_click()
    {
        FakeBrowserPageTransport transport = new()
        {
            Responder = _ => """{"found":true,"x":50,"y":30,"width":40,"height":10,"ok":true}""",
        };
        JsBridgeBrowserDriver driver = new(transport);

        await driver.Click(BrowserTarget.Css("#target"), CursorMotion.Default);

        Assert.IsTrue(transport.Scripts.Any(script => script.Contains("__frFind")), "should locate the element");
        Assert.IsTrue(transport.Scripts.Any(script => script.Contains("__forrest_cursor__")), "should move the visible cursor");
        Assert.IsTrue(transport.Scripts.Any(script => script.Contains("\"click\"")), "should dispatch a synthetic click");
    }

    [TestMethod]
    public async Task Click_with_instant_motion_does_not_move_the_cursor()
    {
        FakeBrowserPageTransport transport = new()
        {
            Responder = _ => """{"found":true,"x":1,"y":2,"ok":true}""",
        };
        JsBridgeBrowserDriver driver = new(transport);

        await driver.Click(BrowserTarget.Css("#target"), CursorMotion.Instant);

        Assert.IsFalse(
            transport.Scripts.Any(script => script.Contains("__forrest_cursor__")),
            "an instant (invisible) motion must not paint the cursor overlay");
        Assert.IsTrue(transport.Scripts.Any(script => script.Contains("\"click\"")));
    }

    [TestMethod]
    public async Task Type_focuses_then_sets_the_value()
    {
        FakeBrowserPageTransport transport = new()
        {
            Responder = _ => """{"found":true,"x":10,"y":10,"ok":true}""",
        };
        JsBridgeBrowserDriver driver = new(transport);

        await driver.Type(BrowserTarget.Css("#name"), "hello", CursorMotion.Instant, TypingCadence.Instant);

        Assert.IsTrue(
            transport.Scripts.Any(script => script.Contains("\"cleartype\"")),
            "should clear the field before typing");
        int charStrokes = transport.Scripts.Count(script => script.Contains("\"typechar\""));
        Assert.AreEqual(5, charStrokes, "should type one character at a time, firing a keystroke per character");
    }

    [TestMethod]
    public async Task Type_paces_keystrokes_when_a_human_cadence_is_used()
    {
        FakeBrowserPageTransport transport = new()
        {
            Responder = _ => """{"found":true,"x":10,"y":10,"ok":true}""",
        };
        JsBridgeBrowserDriver driver = new(transport);

        await driver.Type(BrowserTarget.Css("#name"), "hi", CursorMotion.Instant, new TypingCadence { MinDelayMs = 0, MaxDelayMs = 0, Seed = 1 });

        Assert.AreEqual(2, transport.Scripts.Count(script => script.Contains("\"typechar\"")));
    }

    [TestMethod]
    public async Task GetText_returns_the_operation_value()
    {
        FakeBrowserPageTransport transport = new()
        {
            Responder = _ => """{"ok":true,"value":"Welcome"}""",
        };
        JsBridgeBrowserDriver driver = new(transport);

        string text = await driver.GetText(BrowserTarget.Css("#h1"));

        Assert.AreEqual("Welcome", text);
    }

    [TestMethod]
    public async Task Operation_on_a_missing_element_fails_clearly()
    {
        FakeBrowserPageTransport transport = new()
        {
            Responder = _ => """{"ok":false}""",
        };
        JsBridgeBrowserDriver driver = new(transport);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await driver.Click(BrowserTarget.Css("#missing"), CursorMotion.Instant));
    }

    [TestMethod]
    public async Task Click_animates_the_cursor_along_a_multi_step_path()
    {
        FakeBrowserPageTransport transport = new()
        {
            Responder = _ => """{"found":true,"x":300,"y":200,"width":40,"height":10,"ok":true}""",
        };
        JsBridgeBrowserDriver driver = new(transport);

        await driver.Click(BrowserTarget.Css("#target"), CursorMotion.Default with { StepDelayMs = 0 });

        int cursorMoves = transport.Scripts.Count(script => script.Contains("__forrest_cursor__") && !script.Contains("DOMContentLoaded"));
        Assert.IsGreaterThan(
            1,
            cursorMoves,
            "a human-like move must paint the cursor at many interpolated points, not teleport in one jump");
    }

    [TestMethod]
    public async Task Press_dispatches_a_key_event()
    {
        FakeBrowserPageTransport transport = new();
        JsBridgeBrowserDriver driver = new(transport);

        await driver.Press("Enter");

        Assert.IsTrue(
            transport.Scripts.Any(script => script.Contains("keydown") && script.Contains("Enter")),
            "should dispatch a key event for the named key");
    }

    [TestMethod]
    public async Task Snapshot_parses_the_interactive_surface()
    {
        FakeBrowserPageTransport transport = new()
        {
            Responder = _ => """{"url":"https://x.test/","title":"X","elements":[{"found":true,"tag":"a","text":"Home"}]}""",
        };
        JsBridgeBrowserDriver driver = new(transport);

        BrowserSnapshot snapshot = await driver.Snapshot();

        Assert.AreEqual("https://x.test/", snapshot.Url);
        Assert.AreEqual("X", snapshot.Title);
        Assert.HasCount(1, snapshot.Elements);
    }

    [TestMethod]
    public async Task Screenshot_returns_the_transport_capture()
    {
        FakeBrowserPageTransport transport = new();
        JsBridgeBrowserDriver driver = new(transport);

        string shot = await driver.Screenshot();

        Assert.AreEqual("base64png", shot);
    }

    [TestMethod]
    public async Task Click_prefers_a_trusted_tap_when_the_transport_supports_it()
    {
        TrustedFakeBrowserPageTransport transport = new()
        {
            Responder = _ => """{"found":true,"x":120,"y":48,"width":40,"height":10,"ok":true}""",
            TrustedTapResult = true,
        };
        JsBridgeBrowserDriver driver = new(transport);

        await driver.Click(BrowserTarget.Css("#target"), CursorMotion.Instant);

        Assert.HasCount(1, transport.TrustedTaps);
        Assert.AreEqual((120d, 48d), transport.TrustedTaps[0]);
        Assert.IsFalse(
            transport.Scripts.Any(script => script.Contains("\"click\"")),
            "a delivered trusted tap must replace the synthetic DOM click, not double-fire it");
    }

    [TestMethod]
    public async Task Click_falls_back_to_a_synthetic_click_when_the_trusted_tap_declines()
    {
        TrustedFakeBrowserPageTransport transport = new()
        {
            Responder = _ => """{"found":true,"x":10,"y":10,"ok":true}""",
            TrustedTapResult = false,
        };
        JsBridgeBrowserDriver driver = new(transport);

        await driver.Click(BrowserTarget.Css("#target"), CursorMotion.Instant);

        Assert.HasCount(1, transport.TrustedTaps);
        Assert.IsTrue(
            transport.Scripts.Any(script => script.Contains("\"click\"")),
            "when the trusted tap cannot land, the driver must still click synthetically");
    }

    private sealed class FakeBrowserPageTransport : IBrowserPageTransport
    {
        public List<string> Scripts { get; } = [];

        public List<string> Navigations { get; } = [];

        public Func<string, string>? Responder { get; set; }

        public bool IsAvailable => true;

        public Task<string> Evaluate(string script, CancellationToken cancellationToken = default)
        {
            Scripts.Add(script);
            return Task.FromResult(Responder?.Invoke(script) ?? "{}");
        }

        public Task Navigate(string url, CancellationToken cancellationToken = default)
        {
            Navigations.Add(url);
            return Task.CompletedTask;
        }

        public Task<string> CaptureScreenshot(CancellationToken cancellationToken = default) =>
            Task.FromResult("base64png");
    }

    private sealed class TrustedFakeBrowserPageTransport : IBrowserPageTransport, IBrowserTrustedInput
    {
        public List<string> Scripts { get; } = [];

        public List<(double X, double Y)> TrustedTaps { get; } = [];

        public Func<string, string>? Responder { get; set; }

        public bool TrustedTapResult { get; set; } = true;

        public bool IsAvailable => true;

        public bool SupportsTrustedTap => true;

        public Task<string> Evaluate(string script, CancellationToken cancellationToken = default)
        {
            Scripts.Add(script);
            return Task.FromResult(Responder?.Invoke(script) ?? "{}");
        }

        public Task Navigate(string url, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> CaptureScreenshot(CancellationToken cancellationToken = default) =>
            Task.FromResult("base64png");

        public Task<bool> TryTrustedTap(double cssX, double cssY, CancellationToken cancellationToken = default)
        {
            TrustedTaps.Add((cssX, cssY));
            return Task.FromResult(TrustedTapResult);
        }
    }
}
