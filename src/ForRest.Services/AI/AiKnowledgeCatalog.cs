using ForRest.Scripting;

namespace ForRest.Services.AI;

public interface IAiKnowledgeCatalog
{
    IReadOnlyList<AiKnowledgeDocument> GetDocuments();

    IReadOnlyList<AiPromptTopic> GetTopics();
}

public sealed class ForRestAiKnowledgeCatalog : IAiKnowledgeCatalog
{
    private readonly Lazy<IReadOnlyList<AiKnowledgeDocument>> _documents;
    private readonly Lazy<IReadOnlyList<AiPromptTopic>> _topics;

    public ForRestAiKnowledgeCatalog()
    {
        _documents = new(BuildDocuments);
        _topics = new(BuildTopics);
    }

    public IReadOnlyList<AiKnowledgeDocument> GetDocuments() => _documents.Value;

    public IReadOnlyList<AiPromptTopic> GetTopics() => _topics.Value;

    private static IReadOnlyList<AiKnowledgeDocument> BuildDocuments()
    {
        List<AiKnowledgeDocument> documents =
        [
            new(
                "forrest-language-reference",
                "ForRest Script Language Reference",
                "Canonical markdown reference generated from the Monaco help catalog.",
                ForRestLanguageCatalog.BuildMarkdownReference(),
                ["forrest", "language", "reference", "docs", "monaco"],
                SourcePath: "ForRestLanguageCatalog.BuildMarkdownReference()"),
            new(
                "forrest-language-prompt-context",
                "ForRest Prompt Context",
                "Compact system-prompt context generated from the canonical language catalog.",
                ForRestLanguageCatalog.BuildPromptContext(),
                ["forrest", "prompt", "context", "agent", "system-prompt"],
                SourcePath: "ForRestLanguageCatalog.BuildPromptContext()"),
        ];

        foreach (ForRestLanguageHelpEntry entry in ForRestLanguageCatalog.GetEntries())
        {
            List<string> tags = [entry.Category, entry.Key, .. entry.SearchTerms, .. entry.HoverTerms];
            documents.Add(new(
                $"catalog:{entry.Key}",
                entry.Title,
                entry.Summary,
                BuildEntryContent(entry),
                tags
                    .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                SourcePath: $"ForRestLanguageCatalog:{entry.Key}"));
        }

        return documents;
    }

    private static IReadOnlyList<AiPromptTopic> BuildTopics()
    {
        List<AiPromptTopic> topics =
        [
            new(
                "ForRest prompt context",
                "Compact syntax reference for the assistant system prompt.",
                "ForRestLanguageCatalog.BuildPromptContext()",
                ForRestLanguageCatalog.BuildPromptContext()),
            new(
                "ForRest language reference",
                "Long-form canonical docs source that matches Monaco help content.",
                "ForRestLanguageCatalog.BuildMarkdownReference()",
                ForRestLanguageCatalog.BuildMarkdownReference()),
        ];

        foreach (IGrouping<string, ForRestLanguageHelpEntry> category in ForRestLanguageCatalog
                     .GetEntries()
                     .GroupBy(static entry => entry.Category)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            topics.Add(new(
                $"{category.Key} reference",
                $"Canonical {category.Key.ToLowerInvariant()} entries from the ForRest language catalog.",
                $"ForRestLanguageCatalog:{category.Key}",
                string.Join(
                    Environment.NewLine + Environment.NewLine,
                    category.Select(BuildEntryContent))));
        }

        return topics;
    }

    private static string BuildEntryContent(ForRestLanguageHelpEntry entry)
    {
        List<string> lines =
        [
            $"Title: {entry.Title}",
            $"Category: {entry.Category}",
            $"Summary: {entry.Summary}",
        ];

        if (!string.IsNullOrWhiteSpace(entry.Documentation))
        {
            lines.Add($"Documentation: {entry.Documentation}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Example))
        {
            lines.Add("Example:");
            lines.Add(entry.Example);
        }

        if (entry.SearchTerms.Count > 0)
        {
            lines.Add($"Search terms: {string.Join(", ", entry.SearchTerms)}");
        }

        if (entry.HoverTerms.Count > 0)
        {
            lines.Add($"Hover terms: {string.Join(", ", entry.HoverTerms)}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
