namespace ForRest.Browser;

/// <summary>
/// A thin, typed wrapper over a raw <see cref="ICdpTransport"/>. It builds CDP parameter payloads,
/// issues the calls, and extracts the few result fields the automation engine needs. It holds no
/// page state of its own, which keeps it trivially testable against a fake transport.
/// </summary>
public sealed class CdpClient(ICdpTransport transport)
{
    #region Private Fields

    private const int NavigationTimeoutSeconds = 30;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    #endregion

    #region Public Methods

    /// <summary>Enables the protocol domains the engine relies on and subscribes to load events. Safe to call repeatedly.</summary>
    public async Task EnableDomains(CancellationToken cancellationToken = default)
    {
        await transport.Send("Page.enable", "{}", cancellationToken);
        await transport.Send("DOM.enable", "{}", cancellationToken);
        await transport.Send("Runtime.enable", "{}", cancellationToken);
        transport.Subscribe("Page.loadEventFired");
        transport.Subscribe("Page.frameStoppedLoading");
    }

    /// <summary>
    /// Navigates the page and waits for it to finish loading, so subsequent reads (snapshot/query) see
    /// the real DOM rather than the previous/blank page. Never hangs: it returns after the load event
    /// or a bounded timeout, and honors cancellation.
    /// </summary>
    public async Task Navigate(string url, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnEvent(object? sender, CdpEvent cdpEvent)
        {
            if (cdpEvent.Method is "Page.loadEventFired" or "Page.frameStoppedLoading")
            {
                loaded.TrySetResult();
            }
        }

        transport.EventReceived += OnEvent;
        try
        {
            JsonObject parameters = new() { ["url"] = url };
            await transport.Send("Page.navigate", parameters.ToJsonString(), cancellationToken);

            try
            {
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(NavigationTimeoutSeconds), cancellationToken);
            }
            catch (TimeoutException)
            {
                // The page did not signal load within the budget; proceed so callers are never stuck.
            }
        }
        finally
        {
            transport.EventReceived -= OnEvent;
        }
    }

    /// <summary>
    /// Registers a script to run at the start of every new document (<c>Page.addScriptToEvaluateOnNewDocument</c>).
    /// Navigations wipe injected DOM, so this is how page-level overlays survive across page loads.
    /// </summary>
    public Task AddInitScript(string source, CancellationToken cancellationToken = default)
    {
        JsonObject parameters = new() { ["source"] = source };
        return transport.Send("Page.addScriptToEvaluateOnNewDocument", parameters.ToJsonString(), cancellationToken);
    }

    /// <summary>
    /// Installs the visible red cursor overlay so it stays present while the engine drives the page:
    /// it registers the overlay for every future document and creates it on the current one. The
    /// per-move cursor call still self-heals, but pre-installing means the overlay is there from the
    /// first frame and survives navigations rather than only appearing on the next mouse move.
    /// </summary>
    public async Task InstallCursorOverlay(CancellationToken cancellationToken = default)
    {
        string script = BrowserJs.InstallCursor();
        await AddInitScript(script, cancellationToken);
        await Evaluate(script, cancellationToken: cancellationToken);
    }

    /// <summary>Evaluates a JavaScript expression in the page and returns the result value as raw JSON (or null).</summary>
    public async Task<string?> Evaluate(string expression, bool returnByValue = true, bool awaitPromise = false, CancellationToken cancellationToken = default)
    {
        JsonObject parameters = new()
        {
            ["expression"] = expression,
            ["returnByValue"] = returnByValue,
            ["awaitPromise"] = awaitPromise,
        };
        string response = await transport.Send("Runtime.evaluate", parameters.ToJsonString(), cancellationToken);
        JsonNode? root = Parse(response);
        JsonNode? value = root?["result"]?["result"]?["value"];
        return value?.ToJsonString(SerializerOptions);
    }

    /// <summary>Evaluates an expression that is expected to return a JSON string and parses it.</summary>
    public async Task<JsonNode?> EvaluateJson(string expression, CancellationToken cancellationToken = default)
    {
        string? raw = await Evaluate(expression, cancellationToken: cancellationToken);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        // Runtime.evaluate returns the string value already JSON-encoded; unwrap one layer when present.
        JsonNode? node = Parse(raw);
        if (node is JsonValue value && value.TryGetValue(out string? inner) && !string.IsNullOrEmpty(inner))
        {
            return Parse(inner);
        }

        return node;
    }

    public Task DispatchMouse(string type, double x, double y, string button = "none", int clickCount = 0, CancellationToken cancellationToken = default)
    {
        JsonObject parameters = new()
        {
            ["type"] = type,
            ["x"] = x,
            ["y"] = y,
            ["button"] = button,
            ["clickCount"] = clickCount,
            ["buttons"] = button == "left" ? 1 : 0,
        };
        return transport.Send("Input.dispatchMouseEvent", parameters.ToJsonString(), cancellationToken);
    }

    public Task InsertText(string text, CancellationToken cancellationToken = default)
    {
        JsonObject parameters = new() { ["text"] = text };
        return transport.Send("Input.insertText", parameters.ToJsonString(), cancellationToken);
    }

    /// <summary>
    /// Types a single character as a real key press: a <c>keyDown</c> carrying the character text (which
    /// inserts it and fires <c>keydown</c>/<c>input</c>) followed by a <c>keyUp</c>. This is what makes
    /// per-keystroke typing read as human and lets pages that listen for key events respond as they would
    /// to a person at the keyboard.
    /// </summary>
    public async Task TypeCharacter(string character, CancellationToken cancellationToken = default)
    {
        JsonObject down = new()
        {
            ["type"] = "keyDown",
            ["text"] = character,
            ["key"] = character,
        };
        await transport.Send("Input.dispatchKeyEvent", down.ToJsonString(), cancellationToken);

        JsonObject up = new()
        {
            ["type"] = "keyUp",
            ["key"] = character,
        };
        await transport.Send("Input.dispatchKeyEvent", up.ToJsonString(), cancellationToken);
    }

    public async Task DispatchKey(string key, string code, int virtualKeyCode, string? text, CancellationToken cancellationToken = default)
    {
        JsonObject down = new()
        {
            ["type"] = text is null ? "rawKeyDown" : "keyDown",
            ["key"] = key,
            ["code"] = code,
            ["windowsVirtualKeyCode"] = virtualKeyCode,
            ["nativeVirtualKeyCode"] = virtualKeyCode,
        };
        if (text is not null)
        {
            down["text"] = text;
        }

        await transport.Send("Input.dispatchKeyEvent", down.ToJsonString(), cancellationToken);

        JsonObject up = new()
        {
            ["type"] = "keyUp",
            ["key"] = key,
            ["code"] = code,
            ["windowsVirtualKeyCode"] = virtualKeyCode,
            ["nativeVirtualKeyCode"] = virtualKeyCode,
        };
        await transport.Send("Input.dispatchKeyEvent", up.ToJsonString(), cancellationToken);
    }

    /// <summary>Captures a screenshot and returns the base64-encoded PNG data.</summary>
    public async Task<string> CaptureScreenshot(CancellationToken cancellationToken = default)
    {
        string response = await transport.Send("Page.captureScreenshot", "{\"format\":\"png\"}", cancellationToken);
        JsonNode? root = Parse(response);
        return root?["result"]?["data"]?.GetValue<string>() ?? string.Empty;
    }

    #endregion

    #region Private Methods

    private static JsonNode? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    #endregion
}
