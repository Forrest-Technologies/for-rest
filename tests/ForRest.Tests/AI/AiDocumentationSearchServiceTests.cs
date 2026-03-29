using System.Collections.Generic;
using ForRest.Services.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AiDocumentationSearchServiceTests
{
    [TestMethod]
    public void Search_prefers_ranked_hits_when_the_match_is_strong()
    {
        IAiDocumentationSearchService service = new AiDocumentationSearchService(
        [
            new(
                "ssl",
                "SSL",
                "Certificate validation and HTTPS behavior.",
                "Use ssl true when certificate validation should remain enabled.",
                ["ssl", "certificate", "https"]),
        ]);

        IReadOnlyList<AiKnowledgeSearchHit> hits = service.Search("ssl", 5);

        Assert.AreEqual(1, hits.Count);
        Assert.IsFalse(AiDocumentationSearchService.ShouldFallbackToFullDocs("ssl", hits));
    }

    [TestMethod]
    public void Search_recommends_full_docs_fallback_when_hits_are_missing_or_weak()
    {
        IAiDocumentationSearchService service = new AiDocumentationSearchService(
        [
            new(
                "body",
                "Body",
                "Request body configuration.",
                "A payload can be declared with body json or body text.",
                ["body", "request"]),
        ]);

        IReadOnlyList<AiKnowledgeSearchHit> noHits = service.Search("nonexistent-token", 5);
        IReadOnlyList<AiKnowledgeSearchHit> weakHits = service.Search("payload", 5);

        Assert.AreEqual(0, noHits.Count);
        Assert.IsTrue(AiDocumentationSearchService.ShouldFallbackToFullDocs("nonexistent-token", noHits));
        Assert.AreEqual(1, weakHits.Count);
        Assert.IsTrue(AiDocumentationSearchService.ShouldFallbackToFullDocs("payload", weakHits));
    }
}
