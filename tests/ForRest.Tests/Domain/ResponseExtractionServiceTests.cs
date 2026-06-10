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
    public void Extract_flags_the_variable_secret_when_the_extraction_is_secret()
    {
        var response = new ResponseSnapshot
        {
            Body = """{"access_token":"super-secret-token"}""",
        };

        var result = responseExtractionService.Extract(
            response,
            [
                new()
                {
                    Selector = "$.access_token",
                    TargetVariableName = "token",
                    IsSecret = true,
                },
            ]);

        Assert.HasCount(1, result);
        Assert.AreEqual("super-secret-token", result.Single().Value);
        Assert.IsTrue(result.Single().IsSecret, "an extraction marked secret must produce a secret variable");
    }

    [TestMethod]
    public void Extract_unescapes_json_string_values()
    {
        var response = new ResponseSnapshot
        {
            Body = """{"message":"line1\nsaid \"hi\" to c:\\temp"}""",
        };

        var result = responseExtractionService.Extract(
            response,
            [
                new()
                {
                    Selector = "$.message",
                    TargetVariableName = "message",
                },
            ]);

        Assert.HasCount(1, result);
        Assert.AreEqual("line1\nsaid \"hi\" to c:\\temp", result.Single().Value);
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

    [TestMethod]
    public void Extract_supports_regex_against_body_headers_and_json_selected_values()
    {
        var response = new ResponseSnapshot
        {
            Body = """{"payload":{"id":"42","token":"Bearer abc-123"}}""",
            Headers =
            [
                new()
                {
                    Key = "Set-Cookie",
                    Value = "session=xyz789; Path=/; HttpOnly",
                },
            ],
        };

        var result = responseExtractionService.Extract(
            response,
            [
                new()
                {
                    Source = ExtractionSource.Body,
                    Pattern = @"Bearer ([A-Za-z0-9-]+)",
                    Group = 1,
                    TargetVariableName = "bodyToken",
                },
                new()
                {
                    Source = ExtractionSource.Header,
                    Selector = "set-cookie",
                    Pattern = @"session=([^;]+)",
                    Group = 1,
                    TargetVariableName = "sessionId",
                },
                new()
                {
                    Source = ExtractionSource.Json,
                    Selector = "$.payload.id",
                    Pattern = @"([0-9]+)",
                    Group = 1,
                    TargetVariableName = "idMatch",
                },
            ]);

        Assert.HasCount(3, result);
        Assert.AreEqual("abc-123", result.Single(static item => item.Key == "bodyToken").Value);
        Assert.AreEqual("xyz789", result.Single(static item => item.Key == "sessionId").Value);
        Assert.AreEqual("42", result.Single(static item => item.Key == "idMatch").Value);
    }

    #endregion
}
