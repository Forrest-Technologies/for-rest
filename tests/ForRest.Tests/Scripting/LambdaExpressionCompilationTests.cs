namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class LambdaExpressionCompilationTests
{
    #region Public Methods

    [TestMethod]
    public async Task Block_lambda_locals_do_not_replace_runtime_identifiers_outside_the_lambda()
    {
        List<ForRestScriptDiagnostic> diagnostics = [];
        var generated = ForRestFlowScriptCompiler.Compile("""
            let callback = (Func<string, Task<string>>)(async (string value) => {
                var copy = value;
                await Task.Yield();
                return copy;
            })
            stash.Inner = await callback("inside")
            stash.Outer = value
            stash.Copy = copy
            stash.Commit()
            """, [], [], diagnostics);
        Assert.IsEmpty(diagnostics);
        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);

        var result = await engine.Run(new()
        {
            Script = generated,
            RequestVariables =
            [
                new() { Key = "value", Value = "outside", Scope = VariableScope.RequestLocal },
                new() { Key = "copy", Value = "runtime copy", Scope = VariableScope.RequestLocal },
            ],
        });

        Assert.AreEqual(string.Empty, result.ErrorMessage, result.ErrorMessage);
        Assert.HasCount(1, result.Stash.Rows);
        Assert.AreEqual("inside", result.Stash.Rows[0].Values["Inner"]);
        Assert.AreEqual("outside", result.Stash.Rows[0].Values["Outer"]);
        Assert.AreEqual("runtime copy", result.Stash.Rows[0].Values["Copy"]);
    }

    [TestMethod]
    public async Task Nested_lambda_declarations_do_not_hide_runtime_values_in_the_enclosing_callback()
    {
        List<ForRestScriptDiagnostic> diagnostics = [];
        var generated = ForRestFlowScriptCompiler.Compile("""
            let callback = (Func<string>)(() => {
                var nested = (Func<string>)(() => { var copy = "nested"; return copy; });
                var value = nested();
                return copy + value;
            })
            stash.Result = callback()
            stash.Commit()
            """, [], [], diagnostics);
        Assert.IsEmpty(diagnostics);
        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);

        var result = await engine.Run(new()
        {
            Script = generated,
            RequestVariables = [new() { Key = "copy", Value = "runtime copy", Scope = VariableScope.RequestLocal }],
        });

        Assert.AreEqual(string.Empty, result.ErrorMessage, result.ErrorMessage);
        Assert.HasCount(1, result.Stash.Rows);
        Assert.AreEqual("runtime copynested", result.Stash.Rows[0].Values["Result"]);
    }

    #endregion
}
