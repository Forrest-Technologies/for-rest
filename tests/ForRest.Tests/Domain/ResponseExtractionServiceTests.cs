namespace ForRest.Tests.Domain;

[TestClass]
public sealed class ResponseExtractionServiceTests
{
    #region Private Fields

    private readonly ResponseExtractionService responseExtractionService = new();

    #endregion

    #region Public Methods

    [TestMethod]
    public void Extract_reads_nested_json_values_into_runtime_variables()
    {
        var response = new ResponseSnapshot
        {
            Body = """{"user":{"id":"42","roles":["admin","editor"]}}""",
        };

        var result = responseExtractionService.Extract(
            response,
            [
                new()
                {
                    Selector = "$.user.id",
                    TargetVariableName = "userId",
                },
                new()
                {
                    Selector = "$.user.roles[1]",
                    TargetVariableName = "role",
                },
            ]);

        Assert.HasCount(2, result);
        Assert.AreEqual("42", result.Single(static item => item.Key == "userId").Value);
        Assert.AreEqual("editor", result.Single(static item => item.Key == "role").Value);
    }

    [TestMethod]
    public void Extract_returns_empty_collection_for_invalid_json_or_missing_paths()
    {
        var invalidResult = responseExtractionService.Extract(
            new ResponseSnapshot
            {
                Body = "not-json",
            },
            [
                new()
                {
                    Selector = "$.value",
                    TargetVariableName = "value",
                },
            ]);

        var missingResult = responseExtractionService.Extract(
            new ResponseSnapshot
            {
                Body = """{"value":1}""",
            },
            [
                new()
                {
                    Selector = "$.missing",
                    TargetVariableName = "value",
                },
            ]);

        Assert.IsEmpty(invalidResult);
        Assert.IsEmpty(missingResult);
    }

    #endregion
}
