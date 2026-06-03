namespace AIStudyHub.NUnitDemo.Tests.Domain;

public sealed record StudyDocument(
    Guid Id,
    string Title,
    string Content,
    string OwnerId,
    bool IsProcessed,
    IReadOnlyList<string> Tags);

public sealed record SearchResult(
    Guid DocumentId,
    string Title,
    string Snippet,
    double Score);

public interface IEmbeddingClient
{
    Task<double[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}

public sealed class StudySearchService
{
    private readonly IReadOnlyList<StudyDocument> _documents;
    private readonly IEmbeddingClient _embeddingClient;

    public StudySearchService(IReadOnlyList<StudyDocument> documents, IEmbeddingClient embeddingClient)
    {
        _documents = documents;
        _embeddingClient = embeddingClient;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        string ownerId,
        int limit = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query must not be empty.", nameof(query));
        }

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner id must not be empty.", nameof(ownerId));
        }

        var queryEmbedding = await _embeddingClient.EmbedAsync(query, cancellationToken);
        var normalizedQuery = query.Trim();

        return _documents
            .Where(document => document.OwnerId == ownerId && document.IsProcessed)
            .Select(document => new SearchResult(
                document.Id,
                document.Title,
                CreateSnippet(document.Content, normalizedQuery),
                Score(document, normalizedQuery, queryEmbedding)))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title)
            .Take(limit)
            .ToList();
    }

    private static string CreateSnippet(string content, string query)
    {
        var index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return content.Length <= 80 ? content : content[..80] + "...";
        }

        var start = Math.Max(0, index - 24);
        var length = Math.Min(content.Length - start, query.Length + 48);
        var snippet = content.Substring(start, length).Trim();

        return start > 0 ? "..." + snippet : snippet;
    }

    private static double Score(StudyDocument document, string query, IReadOnlyList<double> queryEmbedding)
    {
        var textScore = 0.0;

        if (document.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            textScore += 3.0;
        }

        if (document.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            textScore += 2.0;
        }

        if (document.Tags.Any(tag => tag.Equals(query, StringComparison.OrdinalIgnoreCase)))
        {
            textScore += 1.0;
        }

        if (textScore == 0)
        {
            return 0;
        }

        return textScore + Math.Min(1.0, queryEmbedding.Sum(Math.Abs) / 10.0);
    }
}
