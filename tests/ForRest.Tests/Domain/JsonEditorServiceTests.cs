namespace ForRest.Tests.Domain;

[TestClass]
public sealed class JsonEditorServiceTests
{
    #region Private Fields

    private readonly JsonEditorService jsonEditorService = new();

    #endregion

    #region Public Methods

    [TestMethod]
    public void Format_pretty_prints_valid_json()
    {
        var result = jsonEditorService.Format("""{"value":1,"items":[true,false]}""");

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Value);
        StringAssert.Contains(result.Value, Environment.NewLine);
        StringAssert.Contains(result.Value, "\"items\"");
    }

    [TestMethod]
    public void Minify_compacts_valid_json()
    {
        var result = jsonEditorService.Minify(
            """
            {
              "value": 1
            }
            """);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("""{"value":1}""", result.Value);
    }

    [TestMethod]
    public void Validate_returns_failure_for_invalid_json()
    {
        var result = jsonEditorService.Validate("{ broken ");

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    #endregion
}
