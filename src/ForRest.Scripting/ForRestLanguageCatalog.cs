using System.Text.Json;

namespace ForRest.Scripting;

public sealed record ForRestLanguageHelpEntry(
    string Key,
    string Title,
    string Category,
    string Summary,
    string Documentation,
    string Example,
    IReadOnlyList<string> SearchTerms,
    IReadOnlyList<string> HoverTerms,
    string MonacoCompletionKind,
    string InsertText,
    bool InsertAsSnippet);

public static class ForRestLanguageCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<ForRestLanguageHelpEntry> GetEntries()
    {
        return ForRestLanguageReference.GetEntries();
    }

    public static string BuildMonacoCatalogJson()
    {
        IReadOnlyList<ForRestMonacoLanguageEntry> entries =
        [
            .. GetEntries().Select(
                entry => new ForRestMonacoLanguageEntry(
                    entry.Key,
                    entry.Title,
                    entry.Category,
                    entry.Summary,
                    entry.Documentation,
                    entry.Example,
                    entry.SearchTerms,
                    entry.HoverTerms,
                    entry.MonacoCompletionKind,
                    entry.InsertText,
                    entry.InsertAsSnippet)),
        ];

        return JsonSerializer.Serialize(entries, JsonOptions);
    }

    public static string BuildMarkdownReference()
    {
        return ForRestLanguageReference.BuildMarkdownReference();
    }

    public static string BuildPromptContext()
    {
        return ForRestLanguageReference.BuildPromptContext();
    }

    private sealed record ForRestMonacoLanguageEntry(
        string Key,
        string Label,
        string Category,
        string Summary,
        string Documentation,
        string Example,
        IReadOnlyList<string> SearchTerms,
        IReadOnlyList<string> HoverTerms,
        string Kind,
        string InsertText,
        bool InsertAsSnippet);
}
