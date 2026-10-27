namespace ForRest.Tests.Services;

[TestClass]
public sealed class ForRestScriptCompilationExtensionsTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("let value = ;")]
    [DataRow("System.String.DoesNotExist();")]
    public void Validation_reports_flow_errors_accepted_by_the_DSL_parser(string flow)
    {
        var compilation = Compile($$"""
            method GET
            url "https://example.test/"
            flow {
                {{flow}}
            }
            """);
        Assert.IsTrue(compilation.Succeeded);

        var result = compilation.ValidateScripts(new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance));

        Assert.IsFalse(result.Succeeded);
        Assert.Contains(diagnostic => diagnostic.Message.Contains("Flow script", StringComparison.Ordinal),
            result.Diagnostics);
    }

    [TestMethod]
    public void Validation_checks_generated_test_scripts()
    {
        var compilation = Compile("method GET\nurl \"https://example.test/\"");
        Assert.IsNotNull(compilation.Payload);
        compilation = compilation with
        {
            Payload = compilation.Payload with
            {
                Request = compilation.Payload.Request with { TestsScript = "dynamic value = ;" },
            },
        };

        var result = compilation.ValidateScripts(new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance));

        Assert.IsFalse(result.Succeeded);
        Assert.Contains(diagnostic => diagnostic.Message.Contains("Tests script", StringComparison.Ordinal),
            result.Diagnostics);
    }

    [TestMethod]
    public void Validation_checks_scenario_scripts_and_names_the_failing_scenario()
    {
        var compilation = Compile("method GET\nurl \"https://example.test/\"") with
        {
            ScenarioPayloads =
            [
                new()
                {
                    ScenarioName = "Broken",
                    Request = new() { PreRequestScript = "dynamic value = ;" },
                },
            ],
        };

        var result = compilation.ValidateScripts(new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance));

        Assert.IsFalse(result.Succeeded);
        Assert.Contains(diagnostic => diagnostic.Message.Contains("Scenario 'Broken'", StringComparison.Ordinal),
            result.Diagnostics);
    }

    [TestMethod]
    public void Validation_does_not_execute_user_code()
    {
        var path = Path.Combine(Path.GetTempPath(), $"for-rest-validation-{Guid.NewGuid():N}.txt");
        var literal = JsonSerializer.Serialize(path);
        var compilation = Compile($$"""
            method GET
            url "https://example.test/"
            flow {
                System.IO.File.WriteAllText({{literal}}, "should not run");
            }
            """);

        var result = compilation.ValidateScripts(new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance));

        Assert.IsTrue(result.Succeeded, string.Join('\n', result.Diagnostics.Select(item => item.Message)));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void Validation_skips_duplicate_and_empty_scripts()
    {
        var compilation = Compile("method GET\nurl \"https://example.test/\"");
        Assert.IsNotNull(compilation.Payload);
        var payload = compilation.Payload with
        {
            Request = compilation.Payload.Request with { PreRequestScript = "var value = 1;" },
        };
        compilation = compilation with { Payload = payload, ScenarioPayloads = [payload, payload] };
        var engine = new ValidationProbe();

        var result = compilation.ValidateScripts(engine);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, engine.ValidationCount);
    }

    [TestMethod]
    public void Validation_preserves_DSL_errors_without_invoking_Roslyn()
    {
        var compilation = Compile("method INVALID\nurl \"https://example.test/\"");
        var engine = new ValidationProbe();

        var result = compilation.ValidateScripts(engine);

        Assert.AreSame(compilation, result);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, engine.ValidationCount);
    }

    #endregion

    #region Private Methods

    private static ForRestScriptCompilationResult Compile(string source) =>
        new ForRestScriptCompiler(new()).Compile(source, new());

    #endregion

    private sealed class ValidationProbe : IScriptEngine
    {
        #region Properties

        public int ValidationCount { get; private set; }

        #endregion

        #region Public Methods

        public ScriptValidationResult Validate(string script)
        {
            ValidationCount++;
            return new();
        }

        public Task<ScriptExecutionResult> Run(
            ScriptExecutionRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Compilation must not execute scripts.");

        #endregion
    }
}
