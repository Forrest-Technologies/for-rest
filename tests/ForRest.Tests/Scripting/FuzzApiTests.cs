using System.Collections.Generic;
using System.Diagnostics;
using ForRest.Scripting;

namespace ForRest.Tests.Scripting;

[TestClass]
public sealed class FuzzApiTests
{
    #region Fingerprint & diff

    [TestMethod]
    public void Fingerprint_of_null_response_is_stable_sentinel()
    {
        FuzzApi fuzz = new(new ConsoleApi());

        FuzzFingerprint fingerprint = fuzz.Fingerprint(null);

        Assert.AreEqual(0, fingerprint.Status);
        Assert.AreEqual(0, fingerprint.SizeBytes);
    }

    [TestMethod]
    public void Diff_flags_status_change()
    {
        FuzzApi fuzz = new(new ConsoleApi());
        FuzzBaseline baseline = fuzz.Baseline(Response(200, 1000, 50));

        FuzzDiff diff = fuzz.Diff(baseline, Response(500, 1000, 50));

        Assert.IsTrue(diff.IsAnomalous);
        Assert.IsTrue(diff.Anomalies.Any(a => a.Contains("status changed")));
    }

    [TestMethod]
    public void Diff_flags_large_size_delta_but_ignores_small_jitter()
    {
        FuzzApi fuzz = new(new ConsoleApi());
        FuzzBaseline baseline = fuzz.Baseline(Response(200, 1000, 50));

        Assert.IsFalse(fuzz.Diff(baseline, Response(200, 1010, 50)).IsAnomalous, "10-byte jitter must not flag");
        Assert.IsTrue(fuzz.Diff(baseline, Response(200, 5000, 50)).IsAnomalous, "5x size must flag");
    }

    [TestMethod]
    public void Diff_flags_time_based_anomaly()
    {
        FuzzApi fuzz = new(new ConsoleApi());
        FuzzBaseline baseline = fuzz.Baseline(Response(200, 1000, 40));

        FuzzDiff diff = fuzz.Diff(baseline, Response(200, 1000, 5040));

        Assert.IsTrue(diff.IsAnomalous);
        Assert.IsTrue(diff.Anomalies.Any(a => a.Contains("timing anomaly")));
    }

    [TestMethod]
    public void Diff_without_baseline_throws()
    {
        FuzzApi fuzz = new(new ConsoleApi());

        Assert.ThrowsExactly<System.InvalidOperationException>(() => fuzz.Diff(null!, Response(200, 100, 10)));
    }

    #endregion

    #region Run

    [TestMethod]
    public async Task Run_records_one_attempt_per_payload_and_clusters()
    {
        FuzzApi fuzz = new(new ConsoleApi());

        FuzzResult result = await fuzz.Run(
            new[] { "a", "b", "c" },
            payload => Task.FromResult<ResponseSnapshot?>(Response(200, 1000, 10)),
            new FuzzOptions { MaxConcurrency = 2, TimeoutMs = 0, DelayMs = 0 },
            "sqli");

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(0, result.AnomalyCount);
        Assert.AreEqual(1, result.Clusters.Count, "identical responses must form one cluster");
        Assert.AreEqual(3, result.Clusters[0].AttemptIndexes.Count);
    }

    [TestMethod]
    public async Task Run_respects_concurrency_bound()
    {
        FuzzApi fuzz = new(new ConsoleApi());
        int current = 0;
        int peakObserved = 0;
        object gate = new();

        FuzzResult result = await fuzz.Run(
            Enumerable.Range(0, 20).Select(i => i.ToString()).ToList(),
            async payload =>
            {
                int running = System.Threading.Interlocked.Increment(ref current);
                lock (gate)
                {
                    peakObserved = System.Math.Max(peakObserved, running);
                }

                await Task.Delay(20);
                System.Threading.Interlocked.Decrement(ref current);
                return Response(200, 100, 1);
            },
            new FuzzOptions { MaxConcurrency = 3, TimeoutMs = 0, DelayMs = 0 });

        Assert.AreEqual(20, result.Count);
        Assert.IsTrue(peakObserved <= 3, $"concurrency bound violated: peak {peakObserved}");
        Assert.IsTrue(result.PeakConcurrency <= 3, $"reported peak {result.PeakConcurrency}");
    }

    [TestMethod]
    public async Task Run_records_timeout_without_throwing()
    {
        FuzzApi fuzz = new(new ConsoleApi());

        FuzzResult result = await fuzz.Run(
            new[] { "slow" },
            async payload =>
            {
                await Task.Delay(5000);
                return Response(200, 100, 1);
            },
            new FuzzOptions { MaxConcurrency = 1, TimeoutMs = 50, DelayMs = 0 });

        Assert.AreEqual(1, result.Count);
        Assert.IsNotNull(result.Attempts[0].Error);
        StringAssert.Contains(result.Attempts[0].Error!, "timeout");
        Assert.AreEqual(1, result.AnomalyCount, "errored attempts surface as findings");
    }

    [TestMethod]
    public async Task Run_records_send_exception_as_attempt_error()
    {
        FuzzApi fuzz = new(new ConsoleApi());

        FuzzResult result = await fuzz.Run(
            new[] { "boom" },
            payload => throw new System.InvalidOperationException("send failed"),
            new FuzzOptions { TimeoutMs = 0 });

        Assert.AreEqual(1, result.Count);
        StringAssert.Contains(result.Attempts[0].Error!, "send failed");
    }

    [TestMethod]
    public async Task Run_diffs_attempts_against_first_successful_response()
    {
        FuzzApi fuzz = new(new ConsoleApi());

        FuzzResult result = await fuzz.Run(
            new[] { "clean", "injected" },
            payload => Task.FromResult<ResponseSnapshot?>(
                payload == "injected" ? Response(500, 9000, 50) : Response(200, 1000, 50)),
            new FuzzOptions { MaxConcurrency = 1, TimeoutMs = 0 });

        Assert.AreEqual(2, result.Count);
        Assert.IsNotNull(result.Baseline);
        Assert.AreEqual(200, result.Baseline!.Fingerprint.Status);
        Assert.AreEqual(1, result.AnomalyCount);
        Assert.AreEqual("injected", result.Findings[0].Payload);
    }

    #endregion

    #region Governance

    [TestMethod]
    public async Task Run_refuses_out_of_scope_target()
    {
        ConsoleApi console = new();
        // request is null in this constructor overload -> Url is null -> out of scope once an allowlist exists.
        FuzzApi fuzz = new(console);
        fuzz.AllowHost("api.example.test");

        FuzzResult result = await fuzz.Run(
            new[] { "p1", "p2" },
            payload => Task.FromResult<ResponseSnapshot?>(Response(200, 100, 1)),
            new FuzzOptions { TimeoutMs = 0 });

        Assert.IsTrue(result.Attempts.All(a => a.Error != null && a.Error.Contains("out-of-scope")));
    }

    [TestMethod]
    public void IsInScope_allows_everything_until_an_allowlist_is_declared()
    {
        FuzzApi fuzz = new(new ConsoleApi());

        Assert.IsTrue(fuzz.IsInScope("https://anything.test/x"));

        fuzz.AllowHost("https://api.example.test/base");
        Assert.IsTrue(fuzz.IsInScope("https://api.example.test/other"));
        Assert.IsTrue(fuzz.IsInScope("api.example.test"));
        Assert.IsFalse(fuzz.IsInScope("https://evil.test/x"));

        fuzz.ClearScope();
        Assert.IsTrue(fuzz.IsInScope("https://evil.test/x"));
    }

    [TestMethod]
    public async Task Run_writes_audit_line_into_console()
    {
        ConsoleApi console = new();
        FuzzApi fuzz = new(console);

        await fuzz.Run(
            new[] { "p" },
            payload => Task.FromResult<ResponseSnapshot?>(Response(200, 100, 1)),
            new FuzzOptions { TimeoutMs = 0 },
            "sqli");

        Assert.IsTrue(console.All().Any(e => e.Message.Contains("[fuzz][audit]") && e.Message.Contains("sqli")));
    }

    #endregion

    #region Helpers

    private static ResponseSnapshot Response(int status, long size, long durationMs) => new()
    {
        StatusCode = status,
        SizeBytes = size,
        DurationMilliseconds = durationMs,
    };

    #endregion
}
