using System.Collections.Generic;
using ForRest.Browser;

namespace ForRest.Tests.Browser;

/// <summary>An <see cref="IBrowserAutomationBridge"/> double that records calls for scripting tests.</summary>
internal sealed class RecordingBrowserBridge : IBrowserAutomationBridge
{
    public List<string> Calls { get; } = [];

    public string TextResult { get; set; } = "hello";

    public bool IsAvailable => true;

    public Task Navigate(string url, CancellationToken cancellationToken = default)
    {
        Calls.Add($"navigate:{url}");
        return Task.CompletedTask;
    }

    public Task<BrowserElementInfo> Query(BrowserTarget target, CancellationToken cancellationToken = default)
    {
        Calls.Add($"query:{target}");
        return Task.FromResult(new BrowserElementInfo { Found = true, Css = "#x" });
    }

    public Task<bool> Exists(BrowserTarget target, CancellationToken cancellationToken = default)
    {
        Calls.Add($"exists:{target}");
        return Task.FromResult(true);
    }

    public Task Click(BrowserTarget target, CursorMotion? motion = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"click:{target}");
        return Task.CompletedTask;
    }

    public Task Type(BrowserTarget target, string text, CursorMotion? motion = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"type:{target}={text}");
        return Task.CompletedTask;
    }

    public Task Press(string keys, CancellationToken cancellationToken = default)
    {
        Calls.Add($"press:{keys}");
        return Task.CompletedTask;
    }

    public Task Hover(BrowserTarget target, CursorMotion? motion = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"hover:{target}");
        return Task.CompletedTask;
    }

    public Task<string> GetText(BrowserTarget target, CancellationToken cancellationToken = default)
    {
        Calls.Add($"getText:{target}");
        return Task.FromResult(TextResult);
    }

    public Task<string> GetAttribute(BrowserTarget target, string name, CancellationToken cancellationToken = default)
    {
        Calls.Add($"getAttribute:{target}/{name}");
        return Task.FromResult("attr");
    }

    public Task<BrowserElementInfo> WaitFor(BrowserTarget target, int timeoutMs, CancellationToken cancellationToken = default)
    {
        Calls.Add($"waitFor:{target}");
        return Task.FromResult(new BrowserElementInfo { Found = true });
    }

    public Task ScrollTo(BrowserTarget target, CancellationToken cancellationToken = default)
    {
        Calls.Add($"scrollTo:{target}");
        return Task.CompletedTask;
    }

    public Task Select(BrowserTarget target, string value, CancellationToken cancellationToken = default)
    {
        Calls.Add($"select:{target}={value}");
        return Task.CompletedTask;
    }

    public Task<BrowserSnapshot> Snapshot(CancellationToken cancellationToken = default)
    {
        Calls.Add("snapshot");
        return Task.FromResult(new BrowserSnapshot { Url = "https://x.test/", Title = "X" });
    }

    public Task<string> Screenshot(CancellationToken cancellationToken = default)
    {
        Calls.Add("screenshot");
        return Task.FromResult("QUJD");
    }

    public Task<string> Evaluate(string expression, CancellationToken cancellationToken = default)
    {
        Calls.Add($"evaluate:{expression}");
        return Task.FromResult("null");
    }
}
