using ForRest.Mcp;

namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class AmbiguousSecretEditTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void An_unchanged_redacted_document_restores_each_repeated_name_to_its_own_value(string newline)
    {
        var original = BuildSource(newline);
        var compilation = new ForRestScriptCompiler(new()).Compile(original, new());
        Assert.IsTrue(compilation.Succeeded);

        var restored = McpSecretRedactor.Restore(original, McpSecretRedactor.Redact(original));

        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void Changed_documents_with_repeated_secret_names_are_rejected_before_restoration(string newline)
    {
        var original = BuildSource(newline);
        var updated = McpSecretRedactor.Redact(original).Replace("method GET", "method POST", StringComparison.Ordinal);

        Assert.IsFalse(McpSecretRedactor.TryRestore(original, updated, out var restored, out var error));
        Assert.AreEqual(string.Empty, restored);
        StringAssert.Contains(error, "repeated secret names");

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => McpSecretRedactor.Restore(original, updated));

        StringAssert.Contains(exception.Message, "repeated secret names");
        Assert.DoesNotContain("fake-alpha-value-1963", exception.Message);
        Assert.DoesNotContain("fake-beta-value-2734", exception.Message);
    }

    #endregion

    #region Private Methods

    private static string BuildSource(string newline)
    {
        return "method GET\nurl \"https://fixture.test/\"\nscenario \"alpha\" {\n  secret token = \"fake-alpha-value-1963\"\n}\nscenario \"beta\" {\n  secret token = \"fake-beta-value-2734\"\n}".Replace("\n", newline, StringComparison.Ordinal);
    }

    #endregion
}
