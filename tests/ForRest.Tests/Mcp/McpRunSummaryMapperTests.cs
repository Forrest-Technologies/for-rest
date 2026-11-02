namespace ForRest.Tests.Mcp;

[TestClass]
public sealed class McpRunSummaryMapperTests
{
    #region Public Methods

    [TestMethod]
    [DataRow("token-9000", "token-9000")]
    [DataRow("key/with spaces", "key%2Fwith%20spaces")]
    [DataRow("tiny", "tiny")]
    public void Summaries_redact_known_secrets_from_target_URLs_and_request_names(string secret, string encoded)
    {
        var target = "https://api.example.test/?key=" + encoded;
        var run = new ExecutionRun
        {
            RequestName = "Request " + secret,
            TargetUri = target,
            ErrorMessage = "Rejected " + secret,
            RuntimeVariables = [new() { Key = "token", Value = secret, IsSecret = true, Scope = VariableScope.Runtime }],
            Response = new() { StatusCode = 403, DurationMilliseconds = 25 },
        };

        var summary = McpRunSummaryMapper.Map(run);

        Assert.AreEqual("https://api.example.test/?key=***", summary.TargetUri);
        Assert.AreEqual("Request ***", summary.RequestName);
        Assert.AreEqual("Rejected ***", summary.ErrorMessage);
        Assert.AreEqual(403, summary.Status);
        Assert.AreEqual(25L, summary.DurationMilliseconds);
        Assert.AreEqual(target, run.TargetUri);
    }

    [TestMethod]
    public void Summaries_preserve_values_not_marked_secret()
    {
        var run = new ExecutionRun
        {
            RequestName = "public-value",
            TargetUri = "https://api.example.test/?key=public-value",
            RuntimeVariables = [new() { Key = "value", Value = "public-value", Scope = VariableScope.Runtime }],
        };

        var summary = McpRunSummaryMapper.Map(run);

        Assert.AreEqual(run.TargetUri, summary.TargetUri);
        Assert.AreEqual(run.RequestName, summary.RequestName);
    }

    #endregion
}
