using ForRest.Services.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AiDocumentPatchRangeTests
{
    #region Public Methods

    [TestMethod]
    [DataRow(1, int.MaxValue)]
    [DataRow(9, int.MaxValue)]
    [DataRow(0, int.MaxValue)]
    [DataRow(int.MaxValue, 2)]
    public void Invalid_ranges_return_a_failure_without_changing_the_source(int start, int length)
    {
        const string source = "0123456789";
        var result = new AiDocumentPatchService().Apply(new("fixture", source, [new(start, length, "replacement")]));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(source, result.PatchedText);
        Assert.IsNotEmpty(result.Errors);
    }

    [TestMethod]
    public void A_null_edit_returns_a_failure_without_changing_the_source()
    {
        var result = new AiDocumentPatchService().Apply(new("fixture", "before", [null!]));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("before", result.PatchedText);
        Assert.IsNotEmpty(result.Errors);
    }

    [TestMethod]
    public void A_valid_insertion_at_the_end_still_succeeds()
    {
        var result = new AiDocumentPatchService().Apply(new("fixture", "before", [new(6, 0, " after")]));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("before after", result.PatchedText);
    }

    #endregion
}
