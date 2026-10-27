using ForRest.Mcp;
using ForRest.Services.Sharing;

namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class MultilineSecretRedactionTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("\"prefix\" +\n    \"fake-private-credential-6723\"", true)]
    [DataRow("string.Concat(\n    \"fake-private-credential-6723\",\n    \"suffix\")", true)]
    [DataRow("\n  {\n    \"token\": \"fake-private-credential-6723\"\n  }", false)]
    [DataRow("\n  [\n    \"fake-private-credential-6723\"\n  ]", false)]
    public void Multiline_secret_values_are_redacted_and_round_trip(string expression, bool validExpression)
    {
        var source = $"method GET\nurl \"https://fixture.test/\"\nflow {{\n  secret vault = {expression}\n  runtime after = \"public-value\"\n}}";
        var compilation = new ForRestScriptCompiler(new()).Compile(source, new());
        Assert.IsTrue(compilation.Succeeded, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        var validation = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance).Validate(compilation.Payload!.Request.PreRequestScript);
        Assert.AreEqual(validExpression, validation.Succeeded, validation.ErrorMessage);

        var redacted = McpSecretRedactor.Redact(source);

        Assert.DoesNotContain("fake-private-credential-6723", redacted);
        Assert.DoesNotContain("fake-private-credential-6723", FrsSecretRedactor.Redact(source));
        StringAssert.Contains(redacted, "runtime after = \"public-value\"");
        Assert.AreEqual(source, McpSecretRedactor.Restore(source, redacted));
    }

    #endregion
}
