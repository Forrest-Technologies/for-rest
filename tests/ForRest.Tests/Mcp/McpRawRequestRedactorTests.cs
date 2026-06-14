using ForRest.Mcp;

namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class McpRawRequestRedactorTests
{
    #region Public Methods

    [TestMethod]
    public void Redact_masks_credential_headers_and_keeps_the_rest()
    {
        string rawRequest = string.Join('\n',
            "POST https://api.example.test/v1/users",
            "Authorization: Bearer abc123",
            "X-Api-Key: key-456",
            "Accept: application/json",
            "",
            "{\"name\":\"demo\"}");

        string redacted = McpRawRequestRedactor.Redact(rawRequest);

        StringAssert.Contains(redacted, "Authorization: ***");
        StringAssert.Contains(redacted, "X-Api-Key: ***");
        StringAssert.Contains(redacted, "Accept: application/json");
        StringAssert.Contains(redacted, "{\"name\":\"demo\"}");
        Assert.IsFalse(redacted.Contains("abc123", System.StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("key-456", System.StringComparison.Ordinal));
    }

    [TestMethod]
    public void Redact_matches_header_names_case_insensitively()
    {
        string redacted = McpRawRequestRedactor.Redact("GET https://x.test\nAUTHORIZATION: Basic Zm9v\ncookie: session=1");

        Assert.IsFalse(redacted.Contains("Zm9v", System.StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("session=1", System.StringComparison.Ordinal));
    }

    [TestMethod]
    public void Redact_leaves_body_lines_untouched_even_when_they_look_like_headers()
    {
        string rawRequest = string.Join('\n',
            "POST https://x.test",
            "Content-Type: application/json",
            "",
            "Authorization: Bearer body-text-not-a-header");

        string redacted = McpRawRequestRedactor.Redact(rawRequest);

        StringAssert.Contains(redacted, "Authorization: Bearer body-text-not-a-header");
    }

    [TestMethod]
    public void Redact_preserves_carriage_returns_and_handles_empty_input()
    {
        string redacted = McpRawRequestRedactor.Redact("GET https://x.test\r\nAuthorization: Bearer abc\r\nAccept: */*");

        StringAssert.Contains(redacted, "Authorization: ***\r\n");
        StringAssert.Contains(redacted, "Accept: */*");
        Assert.AreEqual(string.Empty, McpRawRequestRedactor.Redact(null));
        Assert.AreEqual(string.Empty, McpRawRequestRedactor.Redact(string.Empty));
    }

    #endregion
}
