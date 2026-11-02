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

    [TestMethod]
    public async Task Clone_passes_per_send_cancellation_to_the_host()
    {
        var receivedToken = false;
        var finished = false;
        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);

        var result = await engine.Run(new()
        {
            Script = """
                using var cancellation = new System.Threading.CancellationTokenSource();
                var sending = request.Clone().send(cancellation.Token);
                cancellation.Cancel();
                try { await sending; }
                catch (System.OperationCanceledException) { stash.Cancelled = true; stash.Commit(); }
                """,
            MaxSendIterations = 1,
            SendWithCancellation = async (_, cancellationToken) =>
            {
                receivedToken = cancellationToken.CanBeCanceled;
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return new() { StatusCode = 200 };
                }
                finally
                {
                    finished = true;
                }
            },
        });

        Assert.AreEqual(string.Empty, result.ErrorMessage, result.ErrorMessage);
        Assert.IsTrue(receivedToken);
        Assert.IsTrue(finished);
        Assert.AreEqual(1, result.SendCount);
        Assert.IsEmpty(result.SentResponses);
        Assert.AreEqual("true", result.Stash.Rows.Single().Values["Cancelled"]);
    }

    [TestMethod]
    public async Task A_pre_cancelled_send_does_not_consume_the_shared_budget_or_call_the_host()
    {
        var calls = 0;
        var engine = new RoslynScriptEngine(NullLogger<RoslynScriptEngine>.Instance);
        var result = await engine.Run(new()
        {
            Script = """
                using var cancellation = new System.Threading.CancellationTokenSource();
                cancellation.Cancel();
                try { await request.Clone().send(cancellation.Token); }
                catch (System.OperationCanceledException) { stash.Cancelled = true; stash.Commit(); }
                """,
            MaxSendIterations = 1,
            SendWithCancellation = (_, _) =>
            {
                calls++;
                return Task.FromResult<ResponseSnapshot?>(new() { StatusCode = 200 });
            },
        });

        Assert.AreEqual(string.Empty, result.ErrorMessage, result.ErrorMessage);
        Assert.AreEqual(0, calls);
        Assert.AreEqual(0, result.SendCount);
        Assert.IsEmpty(result.SentRequests);
        Assert.AreEqual("true", result.Stash.Rows.Single().Values["Cancelled"]);
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
