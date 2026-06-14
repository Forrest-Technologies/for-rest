using System;
using System.Text;
using ForRest.Mcp;

namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class McpSecretValueScrubberTests
{
    #region Public Methods

    [TestMethod]
    public void Scrub_replaces_plain_secret_values()
    {
        string scrubbed = McpSecretValueScrubber.Scrub(
            "console: token is hunter-2-secret end",
            ["hunter-2-secret"]);

        Assert.AreEqual("console: token is *** end", scrubbed);
    }

    [TestMethod]
    public void Scrub_replaces_base64_and_url_encoded_forms()
    {
        const string secret = "p@ss word+1";
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
        string urlEncoded = Uri.EscapeDataString(secret);
        string text = $"raw={secret} b64={base64} url={urlEncoded}";

        string scrubbed = McpSecretValueScrubber.Scrub(text, [secret]);

        Assert.AreEqual("raw=*** b64=*** url=***", scrubbed);
    }

    [TestMethod]
    public void Scrub_handles_overlapping_values_longest_first()
    {
        // The longer value must be replaced before the shorter prefix so no tail survives.
        string scrubbed = McpSecretValueScrubber.Scrub(
            "a=secret-alpha-extended b=secret-alpha",
            ["secret-alpha", "secret-alpha-extended"]);

        Assert.AreEqual("a=*** b=***", scrubbed);
    }

    [TestMethod]
    public void Scrub_skips_trivially_short_values()
    {
        string scrubbed = McpSecretValueScrubber.Scrub("status is true, id is 12345", ["true", "12345"]);

        Assert.AreEqual("status is true, id is 12345", scrubbed);
    }

    [TestMethod]
    public void Scrub_matches_json_escaped_forms_inside_serialized_payloads()
    {
        // A secret containing a quote appears escaped inside JSON output.
        const string secret = "pa\"ss-secret";
        string json = """{"message":"value pa\"ss-secret leaked"}""";

        string scrubbed = McpSecretValueScrubber.Scrub(json, [secret]);

        Assert.IsFalse(scrubbed.Contains("ss-secret", StringComparison.Ordinal));
        StringAssert.Contains(scrubbed, "***");
    }

    [TestMethod]
    public void Scrub_handles_null_and_empty_input()
    {
        Assert.AreEqual(string.Empty, McpSecretValueScrubber.Scrub(null, ["whatever-secret"]));
        Assert.AreEqual("text", McpSecretValueScrubber.Scrub("text", null));
        Assert.AreEqual("text", McpSecretValueScrubber.Scrub("text", []));
    }

    #endregion
}
