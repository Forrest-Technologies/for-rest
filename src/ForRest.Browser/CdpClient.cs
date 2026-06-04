namespace ForRest.Browser;

/// <summary>
/// A thin, typed wrapper over a raw <see cref="ICdpTransport"/>. It builds CDP parameter payloads,
/// issues the calls, and extracts the few result fields the automation engine needs. It holds no
/// page state of its own, which keeps it trivially testable against a fake transport.
/// </summary>
public sealed class CdpClient(ICdpTransport transport)
{
    #region Private Fields

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    #endregion

    #region Public Methods

    /// <summary>Enables the protocol domains the engine relies on. Safe to call repeatedly.</summary>
    public async Task EnableDomains(CancellationToken cancellationToken = default)
    {
        await transport.Send("Page.enable", "{}", cancellationToken);
        await transport.Send("DOM.enable", "{}", cancellationToken);
        await transport.Send("Runtime.enable", "{}", cancellationToken);
    }

    public Task Navigate(string url, CancellationToken cancellationToken = default)
    {
        JsonObject parameters = new() { ["url"] = url };
        return transport.Send("Page.navigate", parameters.ToJsonString(), cancellationToken);
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
