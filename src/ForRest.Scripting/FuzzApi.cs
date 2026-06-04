using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace ForRest.Scripting;

/// <summary>
/// The script-facing <c>fuzz</c> object. Runs a payload corpus against the
/// current request with bounded concurrency, captures a baseline, and flags
/// anomalies by diffing each attempt's response fingerprint against that
/// baseline. There is no LLM in the loop: clustering and outlier detection are
/// pure, deterministic functions of (status, size, timing). It reuses the same
/// async send delegate the rest of the scripting surface uses
/// (<c>request.send()</c>), bounds concurrency with a <see cref="SemaphoreSlim"/>,
/// honours a per-attempt timeout via <see cref="CancellationToken"/>, and records
/// (never throws) per-attempt errors.
///
/// For-Rest targets security-minded power users running <em>authorized</em>
/// testing only. Every run logs the payload category it touched into the script
/// console as an audit trail, and an optional in-scope host allowlist refuses
/// targets outside the declared scope.
/// </summary>
public sealed class FuzzApi(ConsoleApi console, ScriptRequestApi? request = null)
{
    #region Private Fields

    private readonly HashSet<string> allowedHosts = new(StringComparer.OrdinalIgnoreCase);

    private bool allowlistEnabled;

    #endregion

    #region Governance

    /// <summary>
    /// Declares the in-scope hosts for this script. Once any host is allowed,
    /// the fuzz runner refuses to send to a target whose host is not on the
    /// list. Accepts bare host names ("api.example.test") or full URLs.
    /// </summary>
    public void AllowHost(string host)
    {
        string normalized = NormalizeHost(host);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("fuzz.AllowHost() requires a host name or URL.");
        }

        allowedHosts.Add(normalized);
        allowlistEnabled = true;
        console.Log($"[fuzz][scope] authorized host added: {normalized}");
    }

    /// <summary>Declares several in-scope hosts at once.</summary>
    public void AllowHosts(IEnumerable? hosts)
    {
        if (hosts is null)
        {
            return;
        }

        foreach (object? host in hosts)
        {
            string text = host?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                AllowHost(text);
            }
        }
    }

    /// <summary>Removes the host allowlist so any target is permitted again.</summary>
    public void ClearScope()
    {
        allowedHosts.Clear();
        allowlistEnabled = false;
    }

    /// <summary>The hosts currently declared in scope.</summary>
    public IReadOnlyCollection<string> AllowedHosts => [.. allowedHosts];

    /// <summary>
    /// Returns true when <paramref name="target"/> (host or URL) is in scope.
    /// Always true when no allowlist has been declared.
    /// </summary>
    public bool IsInScope(string? target)
    {
        if (!allowlistEnabled)
        {
            return true;
        }

        string normalized = NormalizeHost(target);
        return !string.IsNullOrWhiteSpace(normalized) && allowedHosts.Contains(normalized);
    }

    #endregion

    #region Fingerprinting & diffing

    /// <summary>
    /// Produces a stable, comparable fingerprint of a response: status code,
    /// a coarse size bucket (log-scaled so small jitter does not split
    /// clusters), and a timing bucket. Used to cluster attempts and to diff
    /// against a baseline.
    /// </summary>
    public FuzzFingerprint Fingerprint(ResponseSnapshot? response)
    {
        if (response is null)
        {
            return new FuzzFingerprint(0, -1, 0, -1, 0);
        }

        return new FuzzFingerprint(
            response.StatusCode,
            SizeBucket(response.SizeBytes),
            response.SizeBytes,
            TimingBucket(response.DurationMilliseconds),
            response.DurationMilliseconds);
    }

    /// <summary>
    /// Captures a baseline by fingerprinting a known-good response. Pass the
    /// response returned by an unfuzzed <c>request.send()</c>.
    /// </summary>
    public FuzzBaseline Baseline(ResponseSnapshot? response)
    {
        FuzzFingerprint fingerprint = Fingerprint(response);
        return new FuzzBaseline(fingerprint, response?.SizeBytes ?? 0, response?.DurationMilliseconds ?? 0);
    }

    /// <summary>
    /// Diffs a response against a baseline and returns the anomalies it detects:
    /// status changes, large size deltas, and time-based anomalies (the classic
    /// signal for blind/time-based injection).
    /// </summary>
    public FuzzDiff Diff(FuzzBaseline baseline, ResponseSnapshot? response)
    {
        if (baseline is null)
        {
            throw new InvalidOperationException("fuzz.Diff() requires a baseline. Capture one with fuzz.Baseline(...).");
        }

        FuzzFingerprint fingerprint = Fingerprint(response);
        List<string> anomalies = [];

        if (fingerprint.Status != baseline.Fingerprint.Status)
        {
            anomalies.Add($"status changed {baseline.Fingerprint.Status} -> {fingerprint.Status}");
        }

        long sizeDelta = fingerprint.SizeBytes - baseline.SizeBytes;
        if (IsSizeAnomaly(baseline.SizeBytes, fingerprint.SizeBytes))
        {
            anomalies.Add($"size delta {sizeDelta:+#;-#;0} bytes ({baseline.SizeBytes} -> {fingerprint.SizeBytes})");
        }

        if (IsTimingAnomaly(baseline.DurationMilliseconds, fingerprint.DurationMilliseconds))
        {
            anomalies.Add($"timing anomaly {baseline.DurationMilliseconds}ms -> {fingerprint.DurationMilliseconds}ms (possible blind/time-based injection)");
        }

        return new FuzzDiff(fingerprint, [.. anomalies]);
    }

    #endregion

    #region Run

    /// <summary>
    /// Runs <paramref name="payloads"/> against the current request, using
    /// <paramref name="send"/> to fire each attempt. Concurrency is bounded by
    /// <see cref="FuzzOptions.MaxConcurrency"/>, each attempt honours
    /// <see cref="FuzzOptions.TimeoutMs"/>, and a <see cref="FuzzOptions.DelayMs"/>
    /// throttle is applied before each send. Per-attempt errors are recorded,
    /// not thrown. Results are clustered and diffed against the first successful
    /// response (or an explicitly captured baseline) to flag anomalies.
    /// </summary>
    public async Task<FuzzResult> Run(
        IEnumerable payloads,
        Func<string, Task<ResponseSnapshot?>> send,
        FuzzOptions? options = null,
        string category = "")
    {
        ArgumentNullException.ThrowIfNull(payloads);
        ArgumentNullException.ThrowIfNull(send);

        FuzzOptions effectiveOptions = options ?? new FuzzOptions();
        List<string> payloadList = MaterializePayloads(payloads);

        string auditCategory = string.IsNullOrWhiteSpace(category) ? "(uncategorized)" : category.Trim();
        console.Log($"[fuzz][audit] authorized run: category={auditCategory}, payloads={payloadList.Count}, concurrency={effectiveOptions.MaxConcurrency}, timeoutMs={effectiveOptions.TimeoutMs}");

        ConcurrentDictionary<int, FuzzAttempt> attemptsByIndex = new();
        using SemaphoreSlim gate = new(Math.Max(1, effectiveOptions.MaxConcurrency));
        int peakConcurrency = 0;
        int currentConcurrency = 0;

        async Task RunAttempt(int index, string payload)
        {
            await gate.WaitAsync();
            int running = Interlocked.Increment(ref currentConcurrency);
            UpdatePeak(ref peakConcurrency, running);
            try
            {
                if (effectiveOptions.DelayMs > 0)
                {
                    await Task.Delay(effectiveOptions.DelayMs);
                }

                attemptsByIndex[index] = await ExecuteAttempt(index, payload, send, effectiveOptions);
            }
            finally
            {
                Interlocked.Decrement(ref currentConcurrency);
                gate.Release();
            }
        }

        List<Task> tasks = [];
        for (int i = 0; i < payloadList.Count; i++)
        {
            tasks.Add(RunAttempt(i, payloadList[i]));
        }

        await Task.WhenAll(tasks);

        List<FuzzAttempt> attempts =
        [
            .. Enumerable.Range(0, payloadList.Count)
                .Where(attemptsByIndex.ContainsKey)
                .Select(index => attemptsByIndex[index]),
        ];

        return BuildResult(attempts, effectiveOptions, peakConcurrency, auditCategory);
    }

    #endregion

    #region Private Methods

    private async Task<FuzzAttempt> ExecuteAttempt(
        int index,
        string payload,
        Func<string, Task<ResponseSnapshot?>> send,
        FuzzOptions options)
    {
        if (!IsInScope(request?.Url))
        {
            console.Warn($"[fuzz][scope] refused out-of-scope target '{request?.Url}' for payload #{index}");
            return new FuzzAttempt(index, payload, 0, 0, 0, $"out-of-scope target '{request?.Url}'", null);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ResponseSnapshot? response;
            if (options.TimeoutMs > 0)
            {
                using CancellationTokenSource cts = new(options.TimeoutMs);
                response = await send(payload).WaitAsync(cts.Token);
            }
            else
            {
                response = await send(payload);
            }

            stopwatch.Stop();
            long duration = response?.DurationMilliseconds ?? stopwatch.ElapsedMilliseconds;
            return new FuzzAttempt(
                index,
                payload,
                response?.StatusCode ?? 0,
                response?.SizeBytes ?? 0,
                duration,
                null,
                response);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new FuzzAttempt(index, payload, 0, 0, stopwatch.ElapsedMilliseconds, $"timeout after {options.TimeoutMs}ms", null);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new FuzzAttempt(index, payload, 0, 0, stopwatch.ElapsedMilliseconds, exception.Message, null);
        }
    }

    private FuzzResult BuildResult(
        List<FuzzAttempt> attempts,
        FuzzOptions options,
        int peakConcurrency,
        string category)
    {
        FuzzBaseline? baseline = ResolveBaseline(attempts, options);

        List<FuzzFinding> findings = [];
        if (baseline is not null)
        {
            foreach (FuzzAttempt attempt in attempts)
            {
                if (attempt.Error is not null)
                {
                    findings.Add(new FuzzFinding(attempt.Index, attempt.Payload, [attempt.Error]));
                    continue;
                }

                FuzzDiff diff = Diff(baseline, attempt.Response);
                if (diff.Anomalies.Count > 0)
                {
                    findings.Add(new FuzzFinding(attempt.Index, attempt.Payload, diff.Anomalies));
                }
            }
        }
        else
        {
            findings.AddRange(
                attempts
                    .Where(static attempt => attempt.Error is not null)
                    .Select(static attempt => new FuzzFinding(attempt.Index, attempt.Payload, [attempt.Error!])));
        }

        List<FuzzCluster> clusters =
        [
            .. attempts
                .Where(static attempt => attempt.Error is null)
                .GroupBy(attempt => Fingerprint(attempt.Response).ClusterKey())
                .Select(group => new FuzzCluster(
                    group.Key,
                    group.First().Status,
                    [.. group.Select(static attempt => attempt.Index)]))
                .OrderByDescending(static cluster => cluster.AttemptIndexes.Count),
        ];

        if (findings.Count > 0)
        {
            console.Warn($"[fuzz][audit] category={category} flagged {findings.Count} anomalies across {attempts.Count} attempts");
        }
        else
        {
            console.Log($"[fuzz][audit] category={category} completed {attempts.Count} attempts with no anomalies");
        }

        return new FuzzResult(
            [.. attempts],
            [.. findings],
            clusters,
            baseline,
            peakConcurrency);
    }

    private FuzzBaseline? ResolveBaseline(List<FuzzAttempt> attempts, FuzzOptions options)
    {
        if (options.Baseline is not null)
        {
            return options.Baseline;
        }

        FuzzAttempt? first = attempts.FirstOrDefault(static attempt => attempt.Error is null && attempt.Response is not null);
        return first?.Response is null ? null : Baseline(first.Response);
    }

    private static List<string> MaterializePayloads(IEnumerable payloads)
    {
        List<string> output = [];
        foreach (object? item in payloads)
        {
            output.Add(item?.ToString() ?? string.Empty);
        }

        return output;
    }

    private static void UpdatePeak(ref int peak, int candidate)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref peak);
            if (candidate <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref peak, candidate, observed) != observed);
    }

    private static int SizeBucket(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return 0;
        }

        // Log2-style bucket so small jitter stays in one cluster while
        // order-of-magnitude shifts split.
        return (int)Math.Floor(Math.Log2(sizeBytes) * 2);
    }

    private static int TimingBucket(long durationMilliseconds)
    {
        if (durationMilliseconds <= 0)
        {
            return 0;
        }

        // 250ms buckets keep ordinary jitter together; multi-second delays
        // (time-based injection) land in distinct buckets.
        return (int)(durationMilliseconds / 250);
    }

    private static bool IsSizeAnomaly(long baselineSize, long candidateSize)
    {
        long delta = Math.Abs(candidateSize - baselineSize);
        if (delta < 64)
        {
            return false;
        }

        // Flag when the size moves more than 25% of the baseline (or any
        // sizable absolute jump when the baseline is empty).
        long threshold = Math.Max(64, baselineSize / 4);
        return delta >= threshold;
    }

    private static bool IsTimingAnomaly(long baselineMs, long candidateMs)
    {
        // A response that takes >=3s longer than baseline (and at least 3x)
        // is the canonical blind/time-based injection signal.
        long delta = candidateMs - baselineMs;
        if (delta < 3000)
        {
            return false;
        }

        return baselineMs <= 0 || candidateMs >= baselineMs * 3;
    }

    private static string NormalizeHost(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        string trimmed = target.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            return uri.Host;
        }

        // Allow bare host or host:port forms.
        int slashIndex = trimmed.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
        {
            trimmed = trimmed[..slashIndex];
        }

        int colonIndex = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex >= 0)
        {
            trimmed = trimmed[..colonIndex];
        }

        return trimmed;
    }

    #endregion
}

/// <summary>Tuning options for <see cref="FuzzApi.Run"/>.</summary>
public sealed class FuzzOptions
{
    /// <summary>Maximum number of attempts in flight at once. Defaults to 4.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Per-attempt timeout in milliseconds. 0 disables the timeout. Defaults to 30s.</summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>Delay applied before each send (courteous rate limiting). Defaults to 0.</summary>
    public int DelayMs { get; set; }

    /// <summary>
    /// Optional explicit baseline. When unset, the first successful response is
    /// used as the baseline for diffing.
    /// </summary>
    public FuzzBaseline? Baseline { get; set; }
}

/// <summary>A comparable fingerprint of a single response.</summary>
public sealed record FuzzFingerprint(int Status, int SizeBucket, long SizeBytes, int TimingBucket, long DurationMilliseconds)
{
    /// <summary>A stable key that clusters responses by status + size bucket.</summary>
    public string ClusterKey()
    {
        return $"{Status}:{SizeBucket}";
    }
}

/// <summary>A captured known-good response used to diff fuzz attempts.</summary>
public sealed record FuzzBaseline(FuzzFingerprint Fingerprint, long SizeBytes, long DurationMilliseconds);

/// <summary>The result of diffing a single response against a baseline.</summary>
public sealed record FuzzDiff(FuzzFingerprint Fingerprint, IReadOnlyList<string> Anomalies)
{
    public bool IsAnomalous => Anomalies.Count > 0;
}

/// <summary>A single fuzz attempt record.</summary>
public sealed record FuzzAttempt(
    int Index,
    string Payload,
    int Status,
    long Size,
    long DurationMilliseconds,
    string? Error,
    ResponseSnapshot? Response);

/// <summary>A flagged anomaly tied back to the payload that produced it.</summary>
public sealed record FuzzFinding(int Index, string Payload, IReadOnlyList<string> Anomalies);

/// <summary>A cluster of attempts that share a response fingerprint.</summary>
public sealed record FuzzCluster(string Key, int Status, IReadOnlyList<int> AttemptIndexes);

/// <summary>The structured outcome of a fuzz run.</summary>
public sealed record FuzzResult(
    IReadOnlyList<FuzzAttempt> Attempts,
    IReadOnlyList<FuzzFinding> Findings,
    IReadOnlyList<FuzzCluster> Clusters,
    FuzzBaseline? Baseline,
    int PeakConcurrency)
{
    public int Count => Attempts.Count;

    public int AnomalyCount => Findings.Count;

    public bool HasFindings => Findings.Count > 0;

    /// <summary>A compact human-readable summary suitable for the console.</summary>
    public string Summarize()
    {
        StringBuilder builder = new();
        builder.Append($"fuzz: {Attempts.Count} attempts, {Findings.Count} anomalies, {Clusters.Count} clusters");
        if (Findings.Count > 0)
        {
            builder.AppendLine();
            foreach (FuzzFinding finding in Findings)
            {
                builder.AppendLine($"  [#{finding.Index}] {finding.Payload} -> {string.Join("; ", finding.Anomalies)}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
