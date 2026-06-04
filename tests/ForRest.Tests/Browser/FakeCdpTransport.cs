using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using ForRest.Browser;

namespace ForRest.Tests.Browser;

/// <summary>A recording <see cref="ICdpTransport"/> double for driving the automation engine in tests.</summary>
internal sealed class FakeCdpTransport : ICdpTransport
{
    public List<(string Method, string Parameters)> Calls { get; } = [];

    public Func<string, string, string>? Responder { get; set; }

    public event EventHandler<CdpEvent>? EventReceived;

    public Task<string> Send(string method, string parametersJson, CancellationToken cancellationToken = default)
    {
        Calls.Add((method, parametersJson));
        string result = Responder?.Invoke(method, parametersJson) ?? "{}";
        return Task.FromResult(result);
    }

    public void Emit(CdpEvent cdpEvent) => EventReceived?.Invoke(this, cdpEvent);

    /// <summary>Builds the CDP <c>Runtime.evaluate</c> envelope for a script that returned the given JSON string.</summary>
    public static string EvaluateResult(string innerJson)
    {
        JsonObject envelope = new()
        {
            ["result"] = new JsonObject
            {
                ["result"] = new JsonObject
                {
                    ["value"] = innerJson,
                },
            },
        };
        return envelope.ToJsonString();
    }

    public static string LocateResult(double x, double y, string css = "#target", string id = "target")
    {
        JsonObject info = new()
        {
            ["found"] = true,
            ["x"] = x,
            ["y"] = y,
            ["width"] = 40,
            ["height"] = 20,
            ["tag"] = "button",
            ["id"] = id,
            ["text"] = "Save",
            ["css"] = css,
            ["xpath"] = "/html/body/button[1]",
            ["role"] = "button",
            ["name"] = "Save",
        };
        return EvaluateResult(info.ToJsonString());
    }
}
