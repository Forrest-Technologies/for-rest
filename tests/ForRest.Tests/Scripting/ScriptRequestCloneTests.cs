namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class ScriptRequestCloneTests
{
    #region Public Methods

    [TestMethod]
    public async Task Concurrent_clones_share_one_send_budget()
    {
        var actualCalls = 0;
        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);

        var result = await engine.Run(new()
        {
            Script = "await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => request.Clone().send()));",
            MaxSendIterations = 5,
            SendAsync = async _ =>
            {
                Interlocked.Increment(ref actualCalls);
                await Task.Yield();
                return new() { StatusCode = 200 };
            },
        });

        Assert.AreEqual(5, actualCalls);
        Assert.AreEqual(5, result.SendCount);
        Assert.HasCount(5, result.SentResponses);
        StringAssert.Contains(result.ErrorMessage, "exceeded max_send_iterations");
    }

    [TestMethod]
    public async Task Clone_sends_update_the_originating_response_and_remaining_budget()
    {
        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);
        var result = await engine.Run(new()
        {
            Script = """
                await Task.WhenAll(request.Clone().send(), request.Clone().send());
                stash.Status = response.Status;
                stash.Remaining = request.RemainingSendIterations;
                stash.Commit();
                """,
            MaxSendIterations = 2,
            SendAsync = async _ =>
            {
                await Task.Yield();
                return new() { StatusCode = 201 };
            },
        });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.AreEqual(2, result.SendCount);
        Assert.IsNotNull(result.SentResponse);
        Assert.AreSame(result.SentResponses.Last(), result.SentResponse);
        Assert.AreEqual("201", result.Stash.Rows[0].Values["Status"]);
        Assert.AreEqual("0", result.Stash.Rows[0].Values["Remaining"]);
    }

    [TestMethod]
    public async Task Clone_does_not_restore_removed_headers()
    {
        var sent = await SendClone("request.Headers.Remove(\"Authorization\");");

        Assert.DoesNotContain(header => header.Key == "Authorization", sent.Headers);
    }

    [TestMethod]
    public async Task Clone_preserves_duplicate_current_headers()
    {
        var sent = await SendClone("""
            request.Headers.Add("X-Multi", "one");
            request.Headers.Add("X-Multi", "two");
            """);

        string[] expected = ["one", "two"];
        CollectionAssert.AreEqual(expected,
            sent.Headers.Where(header => header.Key == "X-Multi").Select(header => header.Value).ToArray());
    }

    #endregion

    #region Private Methods

    private static async Task<PreparedRequest> SendClone(string setup)
    {
        PreparedRequest? sent = null;
        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);
        var result = await engine.Run(new()
        {
            Script = setup + "\nawait request.Clone().send();",
            PreparedRequest = new() { Headers = [new() { Key = "Authorization", Value = "Bearer fake-test-token" }] },
            MaxSendIterations = 1,
            SendAsync = request =>
            {
                sent = request;
                return Task.FromResult<ResponseSnapshot?>(new() { StatusCode = 200 });
            },
        });

        Assert.AreEqual(string.Empty, result.ErrorMessage);
        Assert.IsNotNull(sent);
        return sent;
    }

    #endregion
}
