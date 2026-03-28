using System.Collections.Generic;
using ForRest.Services.AI;

namespace ForRest.Tests.AI;

[TestClass]
public sealed class AiKnowledgeCatalogTests
{
    [TestMethod]
    public void Knowledge_catalog_exposes_canonical_docs_and_entry_documents()
    {
        IAiKnowledgeCatalog catalog = new ForRestAiKnowledgeCatalog();

        IReadOnlyList<AiKnowledgeDocument> documents = catalog.GetDocuments();

        Assert.IsTrue(documents.Count > 10);
        Assert.IsTrue(documents.Any(static document => document.Id == "forrest-language-reference"));
        Assert.IsTrue(documents.Any(static document => document.Id == "forrest-language-prompt-context"));
        Assert.IsTrue(documents.Any(static document => document.Id == "catalog:ssl"));
        Assert.IsTrue(documents.Any(static document => document.Id == "catalog:workspace-execute"));
        StringAssert.Contains(documents.First(static document => document.Id == "catalog:ssl").Content, "certificate validation");
        StringAssert.Contains(documents.First(static document => document.Id == "catalog:workspace-execute").Content, "workspace.execute");
    }

    [TestMethod]
    public void Knowledge_topics_include_prompt_context_and_markdown_reference()
    {
        IAiKnowledgeCatalog catalog = new ForRestAiKnowledgeCatalog();

        IReadOnlyList<AiPromptTopic> topics = catalog.GetTopics();

        Assert.IsTrue(topics.Count >= 2);
        Assert.IsTrue(topics.Any(static topic => topic.Title == "ForRest prompt context"));
        Assert.IsTrue(topics.Any(static topic => topic.Title == "ForRest language reference"));
        StringAssert.Contains(topics.First(static topic => topic.Title == "ForRest prompt context").Content, "Request surface");
    }
}
