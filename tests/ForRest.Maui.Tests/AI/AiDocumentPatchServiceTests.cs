using ForRest.Services.AI;

namespace ForRest.Maui.Tests.AI;

public sealed class AiDocumentPatchServiceTests
{
    [TestMethod]
    public void Apply_rewrites_text_with_non_overlapping_edits()
    {
        AiDocumentPatchService service = new();

        AiDocumentPatchResult result = service.Apply(
            new AiDocumentPatchRequest(
                "doc-1",
                "hello world",
                [
                    new AiTextEdit(6, 5, "agent"),
                    new AiTextEdit(0, 5, "hi"),
                ]));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("hi agent", result.PatchedText);
    }

    [TestMethod]
    public void Apply_rejects_overlapping_edits()
    {
        AiDocumentPatchService service = new();

        AiDocumentPatchResult result = service.Apply(
            new AiDocumentPatchRequest(
                "doc-1",
                "hello world",
                [
                    new AiTextEdit(0, 4, "hey"),
                    new AiTextEdit(3, 4, "test"),
                ]));

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(result.Errors.ToArray(), "Edits must not overlap.");
        Assert.AreEqual("hello world", result.PatchedText);
    }

    [TestMethod]
    public void Apply_rejects_edits_outside_the_source_bounds()
    {
        AiDocumentPatchService service = new();

        AiDocumentPatchResult result = service.Apply(
            new AiDocumentPatchRequest(
                "doc-1",
                "hello",
                [new AiTextEdit(4, 4, "world")]));

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(result.Errors.ToArray(), "Edit 1 extends beyond the end of the source text.");
    }
}

