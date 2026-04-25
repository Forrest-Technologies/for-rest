using ForRest.Services.AI;

namespace ForRest.Maui.Tests.AI;

[TestClass]
public sealed class AiDocumentationSearchServiceTests
{
    [TestMethod]
    public void Search_returns_best_match_and_respects_limit()
    {
        AiDocumentationSearchService service = new(
        [
            new(
                "docs-1",
                "Request Language",
                "Covers ssl, history, and max_send_iterations.",
                "Use ssl true, history true, and max_send_iterations for bounded request flows.",
                ["ssl", "history", "request"]),
            new(
                "docs-2",
                "Agent Framework",
                "Covers tools and prompt building.",
                "The system prompt is assembled from docs and tools.",
                ["agent", "tools"]),
            new(
                "docs-3",
                "Patch Tool",
                "Covers bounded document edits.",
                "The patch tool applies non-overlapping edits.",
                ["patch", "editor"])
        ]);

        IReadOnlyList<AiKnowledgeSearchHit> results = service.Search("bounded request history", maxResults: 2);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("docs-1", results[0].Document.Id);
        Assert.IsTrue(results[0].Score > results[1].Score);
        StringAssert.Contains(results[0].Excerpt, "history");
    }

    [TestMethod]
    public void Search_returns_empty_for_blank_queries()
    {
        AiDocumentationSearchService service = new([]);

        IReadOnlyList<AiKnowledgeSearchHit> results = service.Search(string.Empty, maxResults: 5);

        Assert.AreEqual(0, results.Count);
    }
}

