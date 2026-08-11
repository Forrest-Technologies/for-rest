using ForRest.Services.Sharing;

namespace ForRest.Tests.Services.Sharing;

[TestClass]
public sealed class FrsSecretRedactorTests
{
    [TestMethod]
    public void Redact_masks_secret_values()
    {
        string source = "name \"Test\"\nsecret api_key = \"sk-live-12345\"\nurl \"https://example.com\"";

        string redacted = FrsSecretRedactor.Redact(source);

        StringAssert.Contains(redacted, "secret api_key = \"***\"");
        Assert.IsFalse(redacted.Contains("sk-live-12345", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Redact_preserves_indentation_and_name()
    {
        string source = "\t  secret token = \"abc\"";

        string redacted = FrsSecretRedactor.Redact(source);

        Assert.AreEqual("\t  secret token = \"***\"", redacted);
    }

    [TestMethod]
    public void Redact_masks_every_secret_line()
    {
        string source = "secret one = \"a\"\nsecret two = \"b\"\nrequest keep = \"c\"";

        string redacted = FrsSecretRedactor.Redact(source);

        Assert.IsFalse(redacted.Contains("\"a\"", StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("\"b\"", StringComparison.Ordinal));
        StringAssert.Contains(redacted, "request keep = \"c\"");
    }

    [TestMethod]
    public void Redact_is_case_insensitive_on_keyword()
    {
        string redacted = FrsSecretRedactor.Redact("SECRET key = \"value\"");

        StringAssert.Contains(redacted, "= \"***\"");
        Assert.IsFalse(redacted.Contains("\"value\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Redact_handles_null_and_empty()
    {
        Assert.AreEqual(string.Empty, FrsSecretRedactor.Redact(null));
        Assert.AreEqual(string.Empty, FrsSecretRedactor.Redact(string.Empty));
    }

    [TestMethod]
    public void Redact_leaves_scripts_without_secrets_untouched()
    {
        string source = "name \"Test\"\nmethod GET\nurl \"https://example.com\"";

        Assert.AreEqual(source, FrsSecretRedactor.Redact(source));
    }

    [TestMethod]
    public void ContainsSecrets_detects_secret_lines()
    {
        Assert.IsTrue(FrsSecretRedactor.ContainsSecrets("secret key = \"value\""));
        Assert.IsTrue(FrsSecretRedactor.ContainsSecrets("name \"x\"\n  secret key = getenv(\"K\")"));
    }

    [TestMethod]
    public void ContainsSecrets_is_false_for_plain_scripts()
    {
        Assert.IsFalse(FrsSecretRedactor.ContainsSecrets("name \"x\"\nurl \"https://example.com\""));
        Assert.IsFalse(FrsSecretRedactor.ContainsSecrets(null));
        Assert.IsFalse(FrsSecretRedactor.ContainsSecrets(string.Empty));
    }

    [TestMethod]
    public void Redacted_script_still_parses()
    {
        string source = "name \"Test\"\nmethod GET\nurl \"https://example.com\"\nsecret api_key = \"sk-live\"";

        FrsParseAssert.Parses(FrsSecretRedactor.Redact(source));
    }
}
