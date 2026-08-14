namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class FlowLanguageEnhancementTests
{
    #region Private Fields

    private readonly RoslynScriptEngine scriptEngine = new(NullLogger<RoslynScriptEngine>.Instance);

    #endregion

    #region Foreach Index

    [TestMethod]
    public void Compile_foreach_with_index_emits_counter_declaration_and_increment()
    {
        var script = CompilePreRequestScript(
            """
            name "Foreach Index Shape"
            method GET
            url "https://api.example.test"

            foreach item, i in [10..12] {
              log $"{i}:{item}"
            }
            """);

        StringAssert.Contains(script, "int i = -1;");
        StringAssert.Contains(script, "foreach (dynamic item in __flow.RangeClosed(10, 12))");
        StringAssert.Contains(script, "i++;");
    }

    [TestMethod]
    public async Task Run_foreach_with_index_produces_zero_based_index_values()
    {
        var script = CompilePreRequestScript(
            """
            name "Foreach Index Run"
            method GET
            url "https://api.example.test"

            flow {
              let parts = ""
              foreach item, i in [10..12] {
                parts = parts + $"{i}:{item}|"
              }
              runtime pairs = parts
            }
            """);

        var result = await RunScript(script);

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("0:10|1:11|2:12|", result.RuntimeVariables.Single(static item => item.Key == "pairs").Value);
    }

    [TestMethod]
    public async Task Run_foreach_with_parenthesized_index_pair_still_iterates()
    {
        var script = CompilePreRequestScript(
            """
            name "Foreach Index Parens"
            method GET
            url "https://api.example.test"

            flow {
              let total = 0
              foreach (value, idx) in [5..7] {
                total = total + idx
              }
              runtime index_sum = total
            }
            """);

        var result = await RunScript(script);

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("3", result.RuntimeVariables.Single(static item => item.Key == "index_sum").Value);
    }

    #endregion

    #region Switch Multi-Value Cases

    [TestMethod]
    public void Compile_switch_case_with_comma_separated_values_emits_or_chain()
    {
        var script = CompilePreRequestScript(
            """
            name "Switch Multi Shape"
            method GET
            url "https://api.example.test"

            let code = 200
            switch code {
              case 200, 201 {
                log "created-or-ok"
              }
              default {
                log "other"
              }
            }
            """);

        StringAssert.Contains(script, "== 200 || ");
        StringAssert.Contains(script, "== 201) {");
    }

    [TestMethod]
    public async Task Run_switch_multi_value_case_matches_each_value_and_default_still_works()
    {
        var script = CompilePreRequestScript(
            """
            name "Switch Multi Run"
            method GET
            url "https://api.example.test"

            let first = 200
            switch first {
              case 200, 201 {
                runtime first_outcome = "matched"
              }
              default {
                runtime first_outcome = "default"
              }
            }

            let second = 201
            switch second {
              case 200, 201 {
                runtime second_outcome = "matched"
              }
              default {
                runtime second_outcome = "default"
              }
            }

            let third = 418
            switch third {
              case 200, 201 {
                runtime third_outcome = "matched"
              }
              default {
                runtime third_outcome = "default"
              }
            }
            """);

        var result = await RunScript(script);

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("matched", result.RuntimeVariables.Single(static item => item.Key == "first_outcome").Value);
        Assert.AreEqual("matched", result.RuntimeVariables.Single(static item => item.Key == "second_outcome").Value);
        Assert.AreEqual("default", result.RuntimeVariables.Single(static item => item.Key == "third_outcome").Value);
    }

    #endregion

    #region Retry Expression Count

    [TestMethod]
    public void Compile_retry_with_expression_count_emits_clamped_count_local()
    {
        var script = CompilePreRequestScript(
            """
            name "Retry Expression Shape"
            method GET
            url "https://api.example.test"

            let attempts = 3
            retry attempts {
              log "attempt"
            }
            """);

        StringAssert.Contains(script, "Math.Max(1, (int)Convert.ToInt64((object)(attempts)));");
        StringAssert.Contains(script, "for (int __retryIdx");
    }

    [TestMethod]
    public void Compile_retry_with_expression_delay_emits_clamped_delay()
    {
        var script = CompilePreRequestScript(
            """
            name "Retry Delay Shape"
            method GET
            url "https://api.example.test"

            let attempts = 2
            let wait = 5
            retry attempts with delay wait {
              log "attempt"
            }
            """);

        StringAssert.Contains(script, "Task.Delay((int)Math.Max(0L, Convert.ToInt64((object)(wait))));");
    }

    [TestMethod]
    public void Compile_retry_with_integer_literal_keeps_fast_path()
    {
        var script = CompilePreRequestScript(
            """
            name "Retry Literal Shape"
            method GET
            url "https://api.example.test"

            retry 3 with delay 250 {
              log "attempt"
            }
            """);

        StringAssert.Contains(script, " < 3; ");
        StringAssert.Contains(script, "Task.Delay(250);");
        Assert.IsFalse(script.Contains("__retryCount", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Run_retry_honors_count_from_a_variable()
    {
        var script = CompilePreRequestScript(
            """
            name "Retry Expression Run"
            method GET
            url "https://api.example.test"

            flow {
              let attempts = 4
              let runs = 0
              retry attempts {
                runs = runs + 1
              }
              runtime run_count = runs
            }
            """);

        var result = await RunScript(script);

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("4", result.RuntimeVariables.Single(static item => item.Key == "run_count").Value);
    }

    #endregion

    #region Range Variable Endpoints

    [TestMethod]
    public void Compile_range_literal_with_identifier_endpoints_emits_range_closed_call()
    {
        var script = CompilePreRequestScript(
            """
            name "Range Shape"
            method GET
            url "https://api.example.test"

            let low = 2
            let high = 5
            let values = [low..high]
            """);

        StringAssert.Contains(script, "__flow.RangeClosed(low, high)");
    }

    [TestMethod]
    public void Compile_range_rewrite_leaves_array_indexing_untouched()
    {
        var script = CompilePreRequestScript(
            """
            name "Range Indexing Shape"
            method GET
            url "https://api.example.test"

            let items = [1..3]
            let first = items[0]
            """);

        StringAssert.Contains(script, "items[0]");
    }

    [TestMethod]
    public async Task Run_range_with_variable_endpoints_iterates_inclusively()
    {
        var script = CompilePreRequestScript(
            """
            name "Range Run"
            method GET
            url "https://api.example.test"

            flow {
              let low = 2
              let high = 5
              let total = 0
              foreach value in [low..high] {
                total = total + value
              }
              runtime range_sum = total
            }
            """);

        var result = await RunScript(script);

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("14", result.RuntimeVariables.Single(static item => item.Key == "range_sum").Value);
    }

    [TestMethod]
    public async Task Run_range_with_reversed_variable_endpoints_counts_down()
    {
        var script = CompilePreRequestScript(
            """
            name "Range Reverse Run"
            method GET
            url "https://api.example.test"

            flow {
              let low = 2
              let high = 5
              let sequence = ""
              foreach value in [high..low] {
                sequence = sequence + value
              }
              runtime range_sequence = sequence
            }
            """);

        var result = await RunScript(script);

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("5432", result.RuntimeVariables.Single(static item => item.Key == "range_sequence").Value);
    }

    [TestMethod]
    public async Task Run_range_with_arithmetic_endpoints_evaluates_expressions()
    {
        var script = CompilePreRequestScript(
            """
            name "Range Arithmetic Run"
            method GET
            url "https://api.example.test"

            flow {
              let size = 3
              let count = 0
              foreach value in [1..size - 1] {
                count = count + 1
              }
              runtime range_count = count
            }
            """);

        var result = await RunScript(script);

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("2", result.RuntimeVariables.Single(static item => item.Key == "range_count").Value);
    }

    #endregion

    #region Stop Statement

    [TestMethod]
    public void Compile_stop_in_main_flow_emits_return_null()
    {
        var script = CompilePreRequestScript(
            """
            name "Stop Shape"
            method GET
            url "https://api.example.test"

            runtime before = "yes"
            stop
            """);

        StringAssert.Contains(script, "return null;");
    }

    [TestMethod]
    public void Compile_stop_inside_define_emits_bare_return()
    {
        var script = CompilePreRequestScript(
            """
            name "Stop Define Shape"
            method GET
            url "https://api.example.test"

            define helper {
              stop
            }

            call helper
            """);

        StringAssert.Contains(script, "async Task __define_helper()");
        StringAssert.Contains(script, "return;");
    }

    [TestMethod]
    public async Task Run_stop_skips_subsequent_main_flow_statements()
    {
        var script = CompilePreRequestScript(
            """
            name "Stop Run"
            method GET
            url "https://api.example.test"

            flow {
              runtime before_stop = "yes"
              stop;
              runtime after_stop = "yes"
            }
            """);

        var result = await RunScript(script);

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("yes", result.RuntimeVariables.Single(static item => item.Key == "before_stop").Value);
        Assert.IsFalse(result.RuntimeVariables.Any(static item => item.Key == "after_stop"));
    }

    [TestMethod]
    public async Task Run_stop_inside_conditional_ends_the_flow_early()
    {
        var script = CompilePreRequestScript(
            """
            name "Stop Conditional Run"
            method GET
            url "https://api.example.test"

            flow {
              let code = 200
              if code == 200 {
                runtime inside_if = "yes"
                stop
              }
              runtime after_if = "yes"
            }
            """);

        var result = await RunScript(script);

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("yes", result.RuntimeVariables.Single(static item => item.Key == "inside_if").Value);
        Assert.IsFalse(result.RuntimeVariables.Any(static item => item.Key == "after_if"));
    }

    [TestMethod]
    public async Task Run_stop_inside_define_exits_the_subroutine_only()
    {
        var script = CompilePreRequestScript(
            """
            name "Stop Define Run"
            method GET
            url "https://api.example.test"

            flow {
              define helper {
                runtime inside_before = "yes"
                stop
                runtime inside_after = "yes"
              }

              call helper
              runtime after_call = "yes"
            }
            """);

        var result = await RunScript(script);

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual("yes", result.RuntimeVariables.Single(static item => item.Key == "inside_before").Value);
        Assert.IsFalse(result.RuntimeVariables.Any(static item => item.Key == "inside_after"));
        Assert.AreEqual("yes", result.RuntimeVariables.Single(static item => item.Key == "after_call").Value);
    }

    #endregion

    #region Helpers

    private static string CompilePreRequestScript(string source)
    {
        var compiler = new ForRestScriptCompiler(new ForRestScriptParser());
        var compilation = compiler.Compile(
            source,
            new()
            {
                WorkspaceId = Guid.NewGuid(),
            });

        Assert.IsTrue(compilation.Succeeded, string.Join(Environment.NewLine, compilation.Diagnostics.Select(static item => item.Message)));
        Assert.IsNotNull(compilation.Payload);
        return compilation.Payload.Request.PreRequestScript;
    }

    private Task<ScriptExecutionResult> RunScript(string script)
    {
        return scriptEngine.Run(
            new()
            {
                Script = script,
                PreparedRequest = new()
                {
                    Uri = new("https://api.example.test"),
                },
                Workspace = new()
                {
                    Name = "Demo",
                },
            });
    }

    #endregion
}
