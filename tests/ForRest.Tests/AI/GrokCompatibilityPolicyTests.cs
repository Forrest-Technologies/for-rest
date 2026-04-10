using System.Text;
using System.Text.Json;
using ForRest.Services.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class GrokCompatibilityPolicyTests
{
    [TestMethod]
    public void SanitizeRequestBody_returns_null_when_no_fields_need_stripping()
    {
        byte[] body = Encoding.UTF8.GetBytes(
            """
            {"model":"grok-4-fast-non-reasoning","messages":[{"role":"user","content":"hi"}]}
            """);

        byte[]? result = AgentFrameworkAiRuntimeFactory.GrokCompatibilityPolicy.SanitizeRequestBody(body);

        Assert.IsNull(result, "sanitizer must not allocate a new body when nothing was removed");
    }

    [TestMethod]
    public void SanitizeRequestBody_strips_every_known_incompatible_top_level_field()
    {
        byte[] body = Encoding.UTF8.GetBytes(
            """
            {
              "model": "grok-4-fast-non-reasoning",
              "messages": [{"role":"user","content":"hi"}],
              "parallel_tool_calls": true,
              "store": true,
              "response_format": {"type":"json_object"},
              "metadata": {"trace":"x"},
              "previous_response_id": "resp-123",
              "reasoning": {"effort":"high"},
              "tools": [{"type":"function","function":{"name":"noop"}}]
            }
            """);

        byte[]? result = AgentFrameworkAiRuntimeFactory.GrokCompatibilityPolicy.SanitizeRequestBody(body);

        Assert.IsNotNull(result);
        using JsonDocument doc = JsonDocument.Parse(result);
        JsonElement root = doc.RootElement;

        Assert.IsFalse(root.TryGetProperty("parallel_tool_calls", out _));
        Assert.IsFalse(root.TryGetProperty("store", out _));
        Assert.IsFalse(root.TryGetProperty("response_format", out _));
        Assert.IsFalse(root.TryGetProperty("metadata", out _));
        Assert.IsFalse(root.TryGetProperty("previous_response_id", out _));
        Assert.IsFalse(root.TryGetProperty("reasoning", out _));

        // Everything else must be preserved exactly.
        Assert.AreEqual("grok-4-fast-non-reasoning", root.GetProperty("model").GetString());
        Assert.AreEqual(JsonValueKind.Array, root.GetProperty("messages").ValueKind);
        Assert.AreEqual(JsonValueKind.Array, root.GetProperty("tools").ValueKind);
    }

    [TestMethod]
    public void SanitizeRequestBody_drops_text_format_subtree_but_preserves_text_siblings()
    {
        byte[] body = Encoding.UTF8.GetBytes(
            """
            {
              "model": "grok-4-fast-non-reasoning",
              "input": [{"role":"user","content":"hi"}],
              "text": {
                "format": {"type":"text"},
                "tracking_id": "abc"
              }
            }
            """);

        byte[]? result = AgentFrameworkAiRuntimeFactory.GrokCompatibilityPolicy.SanitizeRequestBody(body);

        Assert.IsNotNull(result);
        using JsonDocument doc = JsonDocument.Parse(result);
        JsonElement text = doc.RootElement.GetProperty("text");
        Assert.IsFalse(text.TryGetProperty("format", out _));
        Assert.AreEqual("abc", text.GetProperty("tracking_id").GetString());
    }

    [TestMethod]
    public void SanitizeRequestBody_is_safe_for_invalid_json_input()
    {
        byte[] body = Encoding.UTF8.GetBytes("not json at all");

        byte[]? result = AgentFrameworkAiRuntimeFactory.GrokCompatibilityPolicy.SanitizeRequestBody(body);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void SanitizeRequestBody_is_safe_for_empty_input()
    {
        byte[]? result = AgentFrameworkAiRuntimeFactory.GrokCompatibilityPolicy.SanitizeRequestBody([]);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void SanitizeRequestBody_preserves_nested_arrays_that_mention_stripped_field_names_inside_other_objects()
    {
        // `parallel_tool_calls` as a top-level key is removed, but if the
        // same string appears elsewhere (as a description, a tool parameter
        // name, etc.) it must be preserved verbatim.
        byte[] body = Encoding.UTF8.GetBytes(
            """
            {
              "model": "grok-4-fast-non-reasoning",
              "parallel_tool_calls": false,
              "messages": [{"role":"system","content":"Mention parallel_tool_calls in your reply."}]
            }
            """);

        byte[]? result = AgentFrameworkAiRuntimeFactory.GrokCompatibilityPolicy.SanitizeRequestBody(body);

        Assert.IsNotNull(result);
        string rewritten = Encoding.UTF8.GetString(result);
        using JsonDocument doc = JsonDocument.Parse(result);
        Assert.IsFalse(doc.RootElement.TryGetProperty("parallel_tool_calls", out _));
        StringAssert.Contains(rewritten, "parallel_tool_calls in your reply");
    }
}
