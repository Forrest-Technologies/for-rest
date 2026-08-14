namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class AssertionOperatorTests
{
    #region Private Fields

    private readonly RoslynScriptEngine scriptEngine = new(NullLogger<RoslynScriptEngine>.Instance);

    #endregion

    #region Numeric Comparison Compile Shape

    [TestMethod]
    public void Compile_emits_numeric_helper_calls_for_json_body_and_header_comparisons()
    {
        var testsScript = CompileTestsScript(
            """
            expect json "$.count" > 5
            expect body <= 100
            expect header "X-Total" >= 10
            """);

        StringAssert.Contains(testsScript, "tests.AssertNumeric(__jsonValue1 ?? string.Empty, @\">\", @\"5\",");
        StringAssert.Contains(testsScript, "tests.AssertNumeric(response.Body ?? string.Empty, @\"<=\", @\"100\",");
        StringAssert.Contains(testsScript, "tests.AssertNumeric(__headerValue3, @\">=\", @\"10\",");
    }

    [TestMethod]
    public void Compile_keeps_status_numeric_comparisons_inline()
    {
        var testsScript = CompileTestsScript(
            """
            expect status >= 200 "success range"
            """);

        StringAssert.Contains(testsScript, "tests.Assert(response.Status >= 200, @\"success range\");");
    }

    #endregion

    #region Numeric Comparison Execution

    [TestMethod]
    public async Task Json_greater_than_passes_when_the_value_is_larger()
    {
        var result = await RunExpectations(
            """
            expect json "$.count" > 5
            """,
            """{"count": 10}""");

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(TestOutcomeState.Passed, result.Tests.Single().State);
    }

    [TestMethod]
    public async Task Json_greater_than_fails_when_the_value_is_smaller()
    {
        var result = await RunExpectations(
            """
            expect json "$.count" > 5
            """,
            """{"count": 3}""");

        Assert.AreEqual(TestOutcomeState.Failed, result.Tests.Single().State);
        StringAssert.Contains(result.ErrorMessage, "json \"$.count\" > 5");
    }

    [TestMethod]
    public async Task Json_ordering_variants_compare_numerically()
    {
        var result = await RunExpectations(
            """
            expect json "$.count" >= 10 "count at least ten"
            expect json "$.count" < 11 "count below eleven"
            expect json "$.count" <= 10 "count at most ten"
            """,
            """{"count": 10}""");

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.HasCount(3, result.Tests);
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
    }

    [TestMethod]
    public async Task Json_numeric_comparison_fails_with_a_clear_message_when_the_actual_value_is_not_numeric()
    {
        var result = await RunExpectations(
            """
            expect json "$.count" > 5
            """,
            """{"count": "abc"}""");

        Assert.AreEqual(TestOutcomeState.Failed, result.Tests.Single().State);
        StringAssert.Contains(result.Tests.Single().Message, "json \"$.count\" > 5 failed: actual value 'abc' is not numeric");
    }

    [TestMethod]
    public async Task Header_numeric_comparison_passes_and_fails_on_the_parsed_value()
    {
        var passing = await RunExpectations(
            """
            expect header "X-Total" >= 10 "total at least ten"
            """,
            """{"ok":true}""",
            [new() { Key = "X-Total", Value = "12" }]);

        var failing = await RunExpectations(
            """
            expect header "X-Total" >= 10 "total at least ten"
            """,
            """{"ok":true}""",
            [new() { Key = "X-Total", Value = "9" }]);

        Assert.AreEqual(string.Empty, passing.ErrorMessage);
        Assert.AreEqual(TestOutcomeState.Passed, passing.Tests.Single().State);
        Assert.AreEqual(TestOutcomeState.Failed, failing.Tests.Single().State);
    }

    [TestMethod]
    public async Task Body_numeric_comparison_treats_the_whole_body_as_the_number()
    {
        var result = await RunExpectations(
            """
            expect body > 5 "body is a big number"
            """,
            "12");

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(TestOutcomeState.Passed, result.Tests.Single().State);
    }

    #endregion

    #region StartsWith And EndsWith

    [TestMethod]
    public void Compile_emits_ordinal_startswith_and_endswith_assertions()
    {
        var testsScript = CompileTestsScript(
            """
            expect body startswith "hello"
            expect json "$.name" endswith "rest"
            """);

        StringAssert.Contains(testsScript, ".StartsWith(@\"hello\", StringComparison.Ordinal)");
        StringAssert.Contains(testsScript, ".EndsWith(@\"rest\", StringComparison.Ordinal)");
    }

    [TestMethod]
    public async Task Body_startswith_and_endswith_pass_on_matching_affixes()
    {
        var result = await RunExpectations(
            """
            expect body startswith "hello" "greeting prefix"
            expect body endswith "world" "greeting suffix"
            """,
            "hello world");

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.HasCount(2, result.Tests);
        Assert.IsTrue(result.Tests.All(static item => item.State == TestOutcomeState.Passed));
    }

    [TestMethod]
    public async Task Body_startswith_fails_on_a_non_matching_prefix()
    {
        var result = await RunExpectations(
            """
            expect body startswith "world"
            """,
            "hello world");

        Assert.AreEqual(TestOutcomeState.Failed, result.Tests.Single().State);
    }

    [TestMethod]
    public async Task Body_startswith_is_case_sensitive()
    {
        var result = await RunExpectations(
            """
            expect body startswith "Hello"
            """,
            "hello world");

        Assert.AreEqual(TestOutcomeState.Failed, result.Tests.Single().State);
    }

    [TestMethod]
    public async Task Json_startswith_and_endswith_compare_the_selected_value()
    {
        var passing = await RunExpectations(
            """
            expect json "$.name" startswith "for" "name prefix"
            expect json "$.name" endswith "rest" "name suffix"
            """,
            """{"name": "forrest"}""");

        var failing = await RunExpectations(
            """
            expect json "$.name" endswith "for"
            """,
            """{"name": "forrest"}""");

        Assert.AreEqual(string.Empty, passing.ErrorMessage);
        Assert.IsTrue(passing.Tests.All(static item => item.State == TestOutcomeState.Passed));
        Assert.AreEqual(TestOutcomeState.Failed, failing.Tests.Single().State);
    }

    [TestMethod]
    public void Parser_treats_a_trailing_quoted_string_after_startswith_as_the_label()
    {
        var document = ParseValid(
            """
            name "Label Heuristic"
            method GET
            url "https://api.example.test/echo"

            expect json "$.name" startswith "for" "name starts with for"
            expect body endswith "tail"
            """);

        var labeled = document.Tests[0];
        Assert.AreEqual(ForRestScriptComparisonOperator.StartsWith, labeled.Operator);
        Assert.AreEqual("for", ((ForRestScriptStringExpression)labeled.Value!).Value);
        Assert.AreEqual("name starts with for", labeled.Message);

        var unlabeled = document.Tests[1];
        Assert.AreEqual(ForRestScriptComparisonOperator.EndsWith, unlabeled.Operator);
        Assert.AreEqual("tail", ((ForRestScriptStringExpression)unlabeled.Value!).Value);
        Assert.AreEqual("body endswith \"tail\"", unlabeled.Message);
    }

    #endregion

    #region Exists And Not Exists

    [TestMethod]
    public void Compile_emits_symmetric_exists_and_not_exists_json_assertions()
    {
        var testsScript = CompileTestsScript(
            """
            expect json "$.id" exists
            expect json "$.missing" not exists
            """);

        StringAssert.Contains(testsScript, "tests.Assert(!string.IsNullOrWhiteSpace(__jsonValue1)");
        StringAssert.Contains(testsScript, "tests.Assert(string.IsNullOrWhiteSpace(__jsonValue2)");
    }

    [TestMethod]
    public async Task Json_not_exists_passes_when_the_selector_matches_nothing()
    {
        var result = await RunExpectations(
            """
            expect json "$.missing" not exists "no missing field"
            """,
            """{"id": 1}""");

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(TestOutcomeState.Passed, result.Tests.Single().State);
    }

    [TestMethod]
    public async Task Json_not_exists_fails_when_the_selector_matches_a_value()
    {
        var result = await RunExpectations(
            """
            expect json "$.id" not exists
            """,
            """{"id": 1}""");

        Assert.AreEqual(TestOutcomeState.Failed, result.Tests.Single().State);
        StringAssert.Contains(result.ErrorMessage, "json \"$.id\" not exists");
    }

    [TestMethod]
    public async Task Json_exists_still_passes_when_the_selector_matches_a_value()
    {
        var result = await RunExpectations(
            """
            expect json "$.id" exists
            """,
            """{"id": 1}""");

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(TestOutcomeState.Passed, result.Tests.Single().State);
    }

    #endregion

    #region Parse-Time Diagnostics

    [TestMethod]
    public void Parse_rejects_startswith_on_status_with_a_targeted_diagnostic()
    {
        var result = new ForRestScriptParser().Parse(
            """
            name "Invalid Status"
            method GET
            url "https://api.example.test/echo"

            expect status startswith "2"
            """);

        Assert.IsFalse(result.Succeeded);
        var diagnostic = result.Diagnostics.Single(static item => item.Severity == ForRestScriptDiagnosticSeverity.Error);
        StringAssert.Contains(diagnostic.Message, "'startswith' and 'endswith' operators are not supported for status assertions");
    }

    [TestMethod]
    public void Parse_rejects_exists_on_body_with_a_targeted_diagnostic()
    {
        var result = new ForRestScriptParser().Parse(
            """
            name "Invalid Body"
            method GET
            url "https://api.example.test/echo"

            expect body exists
            """);

        Assert.IsFalse(result.Succeeded);
        var diagnostic = result.Diagnostics.Single(static item => item.Severity == ForRestScriptDiagnosticSeverity.Error);
        StringAssert.Contains(diagnostic.Message, "only supported for json assertions");
    }

    [TestMethod]
    public void Parse_rejects_not_exists_on_header_inside_a_tests_block()
    {
        var result = new ForRestScriptParser().Parse(
            """
            name "Invalid Header"
            method GET
            url "https://api.example.test/echo"

            tests {
              expect header "X-Total" not exists "should be rejected"
            }
            """);

        Assert.IsFalse(result.Succeeded);
        var diagnostic = result.Diagnostics.Single(static item => item.Severity == ForRestScriptDiagnosticSeverity.Error);
        StringAssert.Contains(diagnostic.Message, "only supported for json assertions");
    }

    #endregion

    #region Renderer Round Trips

    [TestMethod]
    public void New_operators_survive_the_parse_render_parse_round_trip()
    {
        var document = ParseValid(
            """
            name "Round Trip"
            method GET
            url "https://api.example.test/echo"

            expect body startswith "hello" "greeting prefix"
            expect body endswith "world"
            expect json "$.name" startswith "for"
            expect json "$.name" endswith "rest" "name suffix"
            expect json "$.missing" not exists "nothing there"
            expect json "$.id" not exists
            expect json "$.id" exists
            expect json "$.count" > 5
            expect header "X-Total" >= 10 "total floor"
            """);

        var firstRender = ForRestScriptDocumentRenderer.Render(document);
        var reparsed = ParseValid(firstRender);
        var secondRender = ForRestScriptDocumentRenderer.Render(reparsed);

        Assert.AreEqual(firstRender, secondRender, "rendering must be a fixed point after one parse/render cycle");
        Assert.HasCount(document.Tests.Count, reparsed.Tests);
        for (var index = 0; index < document.Tests.Count; index++)
        {
            Assert.AreEqual(document.Tests[index], reparsed.Tests[index], $"assertion {index} must round-trip");
        }
    }

    #endregion

    #region Helpers

    private static ForRestScriptDocument ParseValid(string source)
    {
        var result = new ForRestScriptParser().Parse(source);
        Assert.IsTrue(
            result.Succeeded,
            $"expected the source to parse cleanly, got: {string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Message))}");
        return result.Document!;
    }

    private static string CompileTestsScript(string expectations)
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var source =
            $$"""
            name "Assertion Operators"
            method GET
            url "https://api.example.test/echo"

            {{expectations}}
            """;

        var result = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Diagnostics.Select(static item => item.Message)));
        return result.Payload!.Request.TestsScript;
    }

    private async Task<ScriptExecutionResult> RunExpectations(
        string expectations,
        string responseBody,
        List<KeyValueDefinition>? responseHeaders = null)
    {
        var testsScript = CompileTestsScript(expectations);
        return await scriptEngine.Run(
            new()
            {
                Script = testsScript,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Response = new()
                {
                    StatusCode = 200,
                    Body = responseBody,
                    ContentType = "application/json",
                    Headers = responseHeaders ?? [],
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });
    }

    #endregion
}
