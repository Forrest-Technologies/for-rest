namespace ForRest.Mcp;

public static class McpRunSummaryMapper
{
    #region Public Methods

    public static ForRestMcpRunSummary Map(ExecutionRun run)
    {
        var secretValues = run.RuntimeVariables
            .Where(variable => variable.IsSecret && !string.IsNullOrEmpty(variable.Value))
            .Select(variable => variable.Value).Distinct(StringComparer.Ordinal).ToArray();
        var scrub = McpSecretValueScrubber.CreateScrubber(secretValues);

        return new(
            Id: run.Id.ToString(),
            RequestName: scrub(run.RequestName),
            State: run.State.ToString(),
            Iteration: run.Iteration,
            StartedUtc: run.StartedUtc,
            CompletedUtc: run.CompletedUtc,
            TargetUri: scrub(run.TargetUri),
            Status: run.Response?.StatusCode,
            DurationMilliseconds: run.Response?.DurationMilliseconds,
            ErrorMessage: scrub(run.ErrorMessage));
    }

    #endregion
}
