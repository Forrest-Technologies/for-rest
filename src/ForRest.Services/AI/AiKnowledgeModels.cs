using System.Text;

namespace ForRest.Services.AI;

public sealed record AiKnowledgeDocument(
    string Id,
    string Title,
    string Summary,
    string Content,
    IReadOnlyList<string> Tags,
    string? SourcePath = null);

public sealed record AiKnowledgeSearchHit(
    AiKnowledgeDocument Document,
    int Score,
    string Excerpt);

public interface IAiDocumentationSearchService
{
    IReadOnlyList<AiKnowledgeSearchHit> Search(string query, int maxResults);
}

public sealed class AiDocumentationSearchService : IAiDocumentationSearchService
{
    private readonly IReadOnlyList<AiKnowledgeDocument> _documents;

    public AiDocumentationSearchService(IEnumerable<AiKnowledgeDocument> documents)
    {
        _documents = [.. (documents ?? throw new ArgumentNullException(nameof(documents)))];
    }

    public IReadOnlyList<AiKnowledgeSearchHit> Search(string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0 || _documents.Count == 0)
        {
            return [];
        }

        List<string> queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
        {
            return [];
        }

        List<AiKnowledgeSearchHit> hits = [];
        foreach (AiKnowledgeDocument document in _documents)
        {
            int score = ScoreDocument(document, queryTokens, query);
            if (score <= 0)
            {
                continue;
            }

            hits.Add(new(document, score, BuildExcerpt(document.Content, queryTokens)));
        }

        return hits
            .OrderByDescending(static hit => hit.Score)
            .ThenBy(static hit => hit.Document.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(maxResults, 20))
            .ToArray();
    }

    private static int ScoreDocument(AiKnowledgeDocument document, IReadOnlyList<string> queryTokens, string query)
    {
        int score = 0;
        string title = document.Title;
        string summary = document.Summary;
        string content = document.Content;
        HashSet<string> tags = new(document.Tags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (summary.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (content.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        foreach (string token in queryTokens)
        {
            if (title.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 12;
            }

            if (summary.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }

            if (content.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (tags.Contains(token))
            {
                score += 6;
            }
        }

        return score;
    }

    private static string BuildExcerpt(string content, IReadOnlyList<string> queryTokens)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        int index = -1;
        string matchedToken = string.Empty;
        foreach (string token in queryTokens.OrderByDescending(static token => token.Length))
        {
            index = content.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                matchedToken = token;
                break;
            }
        }

        if (index < 0)
        {
            return content.Length <= 160 ? content : content[..160].TrimEnd();
        }

        int start = Math.Max(0, index - 48);
        int length = Math.Min(content.Length - start, Math.Max(matchedToken.Length + 96, 120));
        string excerpt = content.Substring(start, length).Trim();
        return excerpt.Length <= 160 ? excerpt : excerpt[..160].TrimEnd();
    }

    private static List<string> Tokenize(string text)
    {
        StringBuilder current = new();
        List<string> tokens = [];

        foreach (char character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(char.ToLowerInvariant(character));
                continue;
            }

            FlushCurrent();
        }

        FlushCurrent();
        return tokens;

        void FlushCurrent()
        {
            if (current.Length == 0)
            {
                return;
            }

            tokens.Add(current.ToString());
            current.Clear();
        }
    }
}

