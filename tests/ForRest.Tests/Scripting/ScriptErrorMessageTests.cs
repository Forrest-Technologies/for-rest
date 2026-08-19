namespace ForRest.Tests.Scripting;

/// <summary>
/// Coverage for the compile-diagnostic humanization in <see cref="RoslynScriptEngine"/>.
/// Users write FRS, not C#, so raw Roslyn output ("script.cs(47,13): error CS0103 ...")
/// must be reshaped into "Script error (line N): ..." messages while keeping the raw
/// diagnostic appended after a "details:" separator for bug reports.
/// </summary>
[TestClass]
public sealed class ScriptErrorMessageTests
{
    #region Private Fields

    private readonly RoslynScriptEngine scriptEngine = new(NullLogger<RoslynScriptEngine>.Instance);

    #endregion

    #region Public Methods

    [TestMethod]
    public void Validate_reports_unknown_name_with_friendly_message()
    {
        var validation = scriptEngine.Validate("""variables.Set("x", someUnknownName);""");

        Assert.IsFalse(validation.Succeeded);
        StringAssert.Contains(validation.ErrorMessage, "Script error (line");
        StringAssert.Contains(validation.ErrorMessage, "Unknown name 'someUnknownName'");
        StringAssert.Contains(validation.ErrorMessage, "let someUnknownName = ...");
        Assert.IsFalse(validation.ErrorMessage.StartsWith("script.cs(", StringComparison.Ordinal));
        Assert.IsFalse(validation.ErrorMessage.StartsWith("GeneratedScript", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_reports_unknown_member_with_friendly_message()
    {
        var validation = scriptEngine.Validate("""strings.NotARealMethod("value");""");

        Assert.IsFalse(validation.Succeeded);
        StringAssert.Contains(validation.ErrorMessage, "Script error (line");
        StringAssert.Contains(validation.ErrorMessage, "'NotARealMethod' is not available");
        StringAssert.Contains(validation.ErrorMessage, "Check the member name.");
        Assert.IsFalse(validation.ErrorMessage.StartsWith("script.cs(", StringComparison.Ordinal));
        Assert.IsFalse(validation.ErrorMessage.StartsWith("GeneratedScript", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_reports_missing_brace_as_incomplete_statement()
    {
        var validation = scriptEngine.Validate(
            """
            if (response.Status == 200)
            {
                console.Log("ok");
            """);

        Assert.IsFalse(validation.Succeeded);
        StringAssert.Contains(validation.ErrorMessage, "Script error (line");
        StringAssert.Contains(validation.ErrorMessage, "Incomplete statement near line");
        StringAssert.Contains(validation.ErrorMessage, "missing closing brace, parenthesis, or unfinished expression");
    }

    [TestMethod]
    public void Validate_keeps_raw_diagnostic_in_details_portion()
    {
        var unknownName = scriptEngine.Validate("""variables.Set("x", someUnknownName);""");
        var missingBrace = scriptEngine.Validate(
            """
            if (response.Status == 200)
            {
                console.Log("ok");
            """);

        StringAssert.Contains(unknownName.ErrorMessage, "details:");
        StringAssert.Contains(unknownName.ErrorMessage, "CS0103");
        StringAssert.Contains(missingBrace.ErrorMessage, "details:");
        StringAssert.Contains(missingBrace.ErrorMessage, "CS1513");
    }

    [TestMethod]
    public void Validate_reports_internal_translation_error_for_generated_identifiers()
    {
        // A raw `__`-prefixed identifier can only come from a transpiler bug, never from
        // user-authored FRS, so the message must ask for a bug report and keep the raw text.
        var validation = scriptEngine.Validate("console.Log(__flow.ToString());");

        Assert.IsFalse(validation.Succeeded);
        StringAssert.Contains(validation.ErrorMessage, "Internal script translation error");
        StringAssert.Contains(validation.ErrorMessage, "report");
        StringAssert.Contains(validation.ErrorMessage, "__flow");
        StringAssert.Contains(validation.ErrorMessage, "CS0103");
    }

    [TestMethod]
    public void Validate_accepts_valid_script_without_diagnostics()
    {
        var validation = scriptEngine.Validate(
            """
            variables.Set("token", "123");
            console.Log("ready");
            """);

        Assert.IsTrue(validation.Succeeded, validation.ErrorMessage);
        Assert.AreEqual(string.Empty, validation.ErrorMessage);
    }

    [TestMethod]
    public async Task Run_reports_friendly_compile_error_with_details()
    {
        var result = await scriptEngine.Run(
            new()
            {
                Script = """variables.Set("x", someUnknownName);""",
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });

        StringAssert.Contains(result.ErrorMessage, "Script error (line");
        StringAssert.Contains(result.ErrorMessage, "Unknown name 'someUnknownName'");
        StringAssert.Contains(result.ErrorMessage, "details:");
        StringAssert.Contains(result.ErrorMessage, "CS0103");
        Assert.IsFalse(result.ErrorMessage.StartsWith("script.cs(", StringComparison.Ordinal));
    }

    #endregion
}
