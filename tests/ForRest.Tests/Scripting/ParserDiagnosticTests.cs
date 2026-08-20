namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class ParserDiagnosticTests
{
    #region Unclosed Section Diagnostics

    [TestMethod]
    public void Unclosed_vars_section_reports_the_opening_line()
    {
        var source =
            """
            name "Sample"
            method GET
            url "https://api.example.test"

            vars {
              request resource_id = "42"
            """;

        var result = new ForRestScriptParser().Parse(source);

        var diagnostic = result.Diagnostics.Single(item => item.Message.Contains("'vars' section"));
        Assert.AreEqual(ForRestScriptDiagnosticSeverity.Error, diagnostic.Severity);
        StringAssert.Contains(diagnostic.Message, "opened on line 5");
        Assert.AreEqual(5, diagnostic.Line);
    }

    [TestMethod]
    public void Unclosed_headers_section_reports_the_opening_line()
    {
        var source =
            """
            method GET
            url "https://api.example.test"

            headers {
              "Accept" = "application/json"
            """;

        var result = new ForRestScriptParser().Parse(source);

        var diagnostic = result.Diagnostics.Single(item => item.Message.Contains("'headers' section"));
        StringAssert.Contains(diagnostic.Message, "opened on line 4");
        Assert.AreEqual(4, diagnostic.Line);
    }

    [TestMethod]
    public void Unclosed_tests_section_reports_the_opening_line()
    {
        var source =
            """
            method GET
            url "https://api.example.test"
            tests {
              expect status == 200
            """;

        var result = new ForRestScriptParser().Parse(source);

        var diagnostic = result.Diagnostics.Single(item => item.Message.Contains("'tests' section"));
        StringAssert.Contains(diagnostic.Message, "opened on line 3");
        Assert.AreEqual(3, diagnostic.Line);
    }

    #endregion

    #region Mistyped Directive Warnings

    [TestMethod]
    public void Mistyped_header_directive_warns_and_still_parses()
    {
        var source =
            """
            method GET
            url "https://api.example.test"
            hedaer "Accept" = "application/json"
            """;

        var result = new ForRestScriptParser().Parse(source);

        var warning = result.Diagnostics.Single(item => item.Severity == ForRestScriptDiagnosticSeverity.Warning);
        StringAssert.Contains(warning.Message, "did you mean 'header'");
        Assert.AreEqual(3, warning.Line);
        Assert.IsNotNull(result.Document, "warnings must not null the document");
        StringAssert.Contains(result.Document.Flow, "hedaer");
    }

    [TestMethod]
    public void Mistyped_expect_directive_warns()
    {
        var source =
            """
            method GET
            url "https://api.example.test"
            expct status == 200
            """;

        var result = new ForRestScriptParser().Parse(source);

        var warning = result.Diagnostics.Single(item => item.Severity == ForRestScriptDiagnosticSeverity.Warning);
        StringAssert.Contains(warning.Message, "did you mean 'expect'");
        Assert.IsNotNull(result.Document);
    }

    [TestMethod]
    public void Flow_statements_do_not_warn()
    {
        var source =
            """
            method GET
            url "https://api.example.test"
            let counter = 1
            foreach item in [1..3] {
              log item
            }
            """;

        var result = new ForRestScriptParser().Parse(source);

        Assert.IsFalse(
            result.Diagnostics.Any(item => item.Severity == ForRestScriptDiagnosticSeverity.Warning),
            "legitimate flow statements must not produce mistyped-directive warnings");
        Assert.IsNotNull(result.Document);
    }

    [TestMethod]
    public void Assignments_and_member_calls_do_not_warn()
    {
        var source =
            """
            method GET
            url "https://api.example.test"
            counter = 2
            request.body = "payload"
            """;

        var result = new ForRestScriptParser().Parse(source);

        Assert.IsFalse(result.Diagnostics.Any(item => item.Severity == ForRestScriptDiagnosticSeverity.Warning));
    }

    [TestMethod]
    public void Unrelated_garbage_does_not_warn()
    {
        var source =
            """
            method GET
            url "https://api.example.test"
            zzzzz "X" = 1
            """;

        var result = new ForRestScriptParser().Parse(source);

        Assert.IsFalse(
            result.Diagnostics.Any(item => item.Severity == ForRestScriptDiagnosticSeverity.Warning),
            "words far from any directive fall through to flow silently");
    }

    [TestMethod]
    public void Valid_header_directive_does_not_warn()
    {
        var source =
            """
            method GET
            url "https://api.example.test"
            header "Accept" = "application/json"
            """;

        var result = new ForRestScriptParser().Parse(source);

        Assert.IsFalse(result.Diagnostics.Any());
        Assert.IsNotNull(result.Document);
        Assert.AreEqual(1, result.Document.Headers.Count);
    }

    #endregion
}
