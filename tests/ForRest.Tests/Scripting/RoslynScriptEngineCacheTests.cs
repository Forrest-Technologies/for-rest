namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class RoslynScriptEngineCacheTests
{
    #region Private Fields

    private readonly RoslynScriptEngine scriptEngine = new(NullLogger<RoslynScriptEngine>.Instance);

    #endregion

    #region Public Methods

    [TestMethod]
    public async Task Run_reuses_cached_compilation_for_identical_script_text()
    {
        var script =
            """
            variables.Set("code", $"{response.Status}");
            console.Log($"status:{response.Status}");
            """;

        var firstResult = await scriptEngine.Run(BuildRequest(script, statusCode: 200));
        var secondResult = await scriptEngine.Run(BuildRequest(script, statusCode: 404));

        Assert.AreEqual(string.Empty, firstResult.ErrorMessage);
        Assert.AreEqual(string.Empty, secondResult.ErrorMessage);
        Assert.AreEqual("200", firstResult.RuntimeVariables.Single(static item => item.Key == "code").Value);
        Assert.AreEqual("404", secondResult.RuntimeVariables.Single(static item => item.Key == "code").Value);
        Assert.AreEqual("status:200", firstResult.ConsoleEntries.Single().Message);
        Assert.AreEqual("status:404", secondResult.ConsoleEntries.Single().Message);
        Assert.AreEqual(1, scriptEngine.CompilationCount);
        Assert.AreEqual(1, scriptEngine.CacheSize);
    }

    [TestMethod]
    public async Task Run_compiles_each_distinct_script_text_separately()
    {
        var firstResult = await scriptEngine.Run(BuildRequest("""variables.Set("first", "1");"""));
        var secondResult = await scriptEngine.Run(BuildRequest("""variables.Set("second", "2");"""));

        Assert.AreEqual(string.Empty, firstResult.ErrorMessage);
        Assert.AreEqual(string.Empty, secondResult.ErrorMessage);
        Assert.AreEqual(2, scriptEngine.CompilationCount);
        Assert.AreEqual(2, scriptEngine.CacheSize);
    }

    [TestMethod]
    public async Task Run_does_not_cache_scripts_that_fail_to_compile()
    {
        var brokenResult = await scriptEngine.Run(BuildRequest("this is not valid csharp;"));

        StringAssert.Contains(brokenResult.ErrorMessage, "CS");
        Assert.AreEqual(1, scriptEngine.CompilationCount);
        Assert.AreEqual(0, scriptEngine.CacheSize);

        var fixedResult = await scriptEngine.Run(BuildRequest("""variables.Set("fixed", "yes");"""));

        Assert.AreEqual(string.Empty, fixedResult.ErrorMessage);
        Assert.AreEqual("yes", fixedResult.RuntimeVariables.Single(static item => item.Key == "fixed").Value);
        Assert.AreEqual(2, scriptEngine.CompilationCount);
        Assert.AreEqual(1, scriptEngine.CacheSize);
    }

    [TestMethod]
    public async Task Run_handles_concurrent_first_time_executions_of_the_same_script()
    {
        var script =
            """
            variables.Set("code", $"{response.Status}");
            """;
        var runs = Enumerable.Range(0, 8)
            .Select(index => scriptEngine.Run(BuildRequest(script, statusCode: 200 + index)))
            .ToArray();

        var results = await Task.WhenAll(runs);

        for (var index = 0; index < results.Length; index++)
        {
            Assert.AreEqual(string.Empty, results[index].ErrorMessage);
            Assert.AreEqual(
                $"{200 + index}",
                results[index].RuntimeVariables.Single(static item => item.Key == "code").Value);
        }

        Assert.AreEqual(1, scriptEngine.CacheSize);
        Assert.IsTrue(
            scriptEngine.CompilationCount is >= 1 and <= 8,
            $"Expected between 1 and 8 compilations, observed {scriptEngine.CompilationCount}.");

        var followUpBaseline = scriptEngine.CompilationCount;
        var followUpResult = await scriptEngine.Run(BuildRequest(script));

        Assert.AreEqual(string.Empty, followUpResult.ErrorMessage);
        Assert.AreEqual(followUpBaseline, scriptEngine.CompilationCount);
    }

    [TestMethod]
    public async Task Run_evicts_least_recently_used_entry_when_capacity_is_exceeded()
    {
        scriptEngine.CacheCapacity = 2;
        var scriptA = """variables.Set("script", "a");""";
        var scriptB = """variables.Set("script", "b");""";
        var scriptC = """variables.Set("script", "c");""";

        await scriptEngine.Run(BuildRequest(scriptA));
        await scriptEngine.Run(BuildRequest(scriptB));
        await scriptEngine.Run(BuildRequest(scriptC));

        Assert.AreEqual(3, scriptEngine.CompilationCount);
        Assert.AreEqual(2, scriptEngine.CacheSize);

        var evictedResult = await scriptEngine.Run(BuildRequest(scriptA));

        Assert.AreEqual(string.Empty, evictedResult.ErrorMessage);
        Assert.AreEqual("a", evictedResult.RuntimeVariables.Single(static item => item.Key == "script").Value);
        Assert.AreEqual(4, scriptEngine.CompilationCount);

        var stillCachedResult = await scriptEngine.Run(BuildRequest(scriptC));

        Assert.AreEqual(string.Empty, stillCachedResult.ErrorMessage);
        Assert.AreEqual(4, scriptEngine.CompilationCount);
    }

    [TestMethod]
    public async Task ClearCache_forces_recompilation_on_next_run()
    {
        var script = """variables.Set("cleared", "ok");""";

        await scriptEngine.Run(BuildRequest(script));

        Assert.AreEqual(1, scriptEngine.CompilationCount);
        Assert.AreEqual(1, scriptEngine.CacheSize);

        scriptEngine.ClearCache();

        Assert.AreEqual(0, scriptEngine.CacheSize);

        var rerunResult = await scriptEngine.Run(BuildRequest(script));

        Assert.AreEqual(string.Empty, rerunResult.ErrorMessage);
        Assert.AreEqual(2, scriptEngine.CompilationCount);
        Assert.AreEqual(1, scriptEngine.CacheSize);
    }

    [TestMethod]
    public async Task Validate_reuses_cached_compilation_from_a_previous_run()
    {
        var script = """variables.Set("validated", "ok");""";

        await scriptEngine.Run(BuildRequest(script));

        Assert.AreEqual(1, scriptEngine.CompilationCount);

        var validation = scriptEngine.Validate(script);

        Assert.IsTrue(validation.Succeeded);
        Assert.AreEqual(1, scriptEngine.CompilationCount);
    }

    #endregion

    #region Private Methods

    private static ScriptExecutionRequest BuildRequest(string script, int statusCode = 200) => new()
    {
        Script = script,
        PreparedRequest = new()
        {
            Uri = new("https://api.example.test"),
        },
        Response = new()
        {
            StatusCode = statusCode,
            Body = "{}",
            ContentType = "application/json",
        },
        Workspace = new()
        {
            Name = "CacheTests",
        },
    };

    #endregion
}
