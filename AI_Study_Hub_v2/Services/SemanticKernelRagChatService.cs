using System.Diagnostics;
using System.Text;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class SemanticKernelRagChatService : IAiChatService
{
    public const string NoSourcesAnswer = "I could not find relevant information in your indexed documents.";

    private const string SystemPrompt =
        "You are AI Study Hub, a study assistant. Answer only using the provided source excerpts. " +
        "If the answer is not supported by the excerpts, say that the indexed documents do not contain enough information. " +
        "Cite every factual claim with source markers like [S1]. Do not invent citations, page numbers, or facts. " +
        "Keep the answer concise and study-friendly.";

    private readonly IRagSearchService _ragSearchService;
    private readonly IAiChatCompletionClient _completionClient;
    private readonly RagOptions _ragOptions;
    private readonly GroqOptions _groqOptions;
    private readonly ILogger<SemanticKernelRagChatService> _logger;

    public SemanticKernelRagChatService(
        IRagSearchService ragSearchService,
        IAiChatCompletionClient completionClient,
        IOptions<RagOptions> ragOptions,
        IOptions<GroqOptions> groqOptions,
        ILogger<SemanticKernelRagChatService> logger)
    {
        _ragSearchService = ragSearchService;
        _completionClient = completionClient;
        _ragOptions = ragOptions.Value;
        _groqOptions = groqOptions.Value;
        _logger = logger;
    }

    public async Task<AiChatAnswerResponse> AskAsync(
        Guid supabaseUserId,
        AiChatAskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var question = request.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new AiChatException(400, "question_required", "Question is required.");
        }

        var ragRequest = new RagSearchRequest(
            question,
            request.DocumentId,
            request.FolderId,
            NormalizeFilter(request.SubjectCode),
            NormalizeFilter(request.Semester),
            NormalizeTopK(request.TopK),
            request.DocumentIds);

        IReadOnlyList<RagSearchResultDto> searchResults;
        try
        {
            searchResults = await _ragSearchService.SearchAsync(supabaseUserId, ragRequest, cancellationToken);
        }
        catch (DocumentException ex)
        {
            throw new AiChatException(ex.StatusCode, ex.Code, ex.Message);
        }

        var sources = MapSources(searchResults);
        if (sources.Count == 0)
        {
            return new AiChatAnswerResponse(
                NoSourcesAnswer,
                Array.Empty<AiChatSourceDto>(),
                RefusalReason: "no_sources",
                DurationMs: stopwatch.ElapsedMilliseconds);
        }

        var completionRequest = new AiChatCompletionRequest(
            SystemPrompt,
            BuildUserPrompt(question, sources));

        string answer;
        try
        {
            answer = await _completionClient.CompleteAsync(completionRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_groqOptions.UseLocalDemoFallback)
            {
                _logger.LogWarning(ex,
                    "AI chat provider failed; using local demo fallback answer because Groq:UseLocalDemoFallback is enabled.");
                answer = BuildLocalDemoFallbackAnswer(question, sources);
            }
            else
            {
                _logger.LogError(ex, "AI chat provider failed while answering a grounded RAG question.");
                throw new AiChatException(
                    StatusCodes.Status503ServiceUnavailable,
                    "ai_provider_unavailable",
                    "The AI provider is currently unavailable. Please try again later.");
            }
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new AiChatException(
                StatusCodes.Status503ServiceUnavailable,
                "ai_provider_unavailable",
                "The AI provider returned an empty answer. Please try again later.");
        }

        return new AiChatAnswerResponse(
            answer.Trim(),
            sources,
            RefusalReason: null,
            DurationMs: stopwatch.ElapsedMilliseconds);
    }

    private IReadOnlyList<AiChatSourceDto> MapSources(IReadOnlyList<RagSearchResultDto> searchResults)
    {
        if (searchResults.Count == 0)
        {
            return Array.Empty<AiChatSourceDto>();
        }

        var sources = new List<AiChatSourceDto>(searchResults.Count);
        var remainingContextChars = Math.Max(500, _ragOptions.MaxContextChars);

        for (var i = 0; i < searchResults.Count && remainingContextChars > 0; i++)
        {
            var result = searchResults[i];
            var excerpt = NormalizeWhitespace(result.ContentExcerpt);
            if (string.IsNullOrWhiteSpace(excerpt))
            {
                continue;
            }

            if (excerpt.Length > remainingContextChars)
            {
                excerpt = Truncate(excerpt, remainingContextChars);
            }

            remainingContextChars = Math.Max(0, remainingContextChars - excerpt.Length);

            sources.Add(new AiChatSourceDto(
                $"S{sources.Count + 1}",
                result.DocumentId,
                result.FileName,
                result.ChunkIndex,
                result.PageNumber,
                excerpt,
                result.Score));
        }

        return sources;
    }

    private string BuildUserPrompt(string question, IReadOnlyList<AiChatSourceDto> sources)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Student question:");
        sb.AppendLine(question);
        sb.AppendLine();
        sb.AppendLine("Source excerpts:");

        foreach (var source in sources)
        {
            sb.Append('[').Append(source.Label).Append("] ");
            sb.Append(source.FileName);
            if (source.PageNumber.HasValue)
            {
                sb.Append(", page ").Append(source.PageNumber.Value);
            }
            sb.Append(", chunk ").Append(source.ChunkIndex).AppendLine();
            sb.AppendLine(source.Excerpt);
            sb.AppendLine();
        }

        sb.AppendLine("Instructions:");
        sb.AppendLine("- Answer only from the source excerpts above.");
        sb.AppendLine("- Cite each factual claim with source markers such as [S1] or [S2].");
        sb.AppendLine("- If the excerpts do not contain enough information, say the indexed documents do not contain enough information.");
        sb.AppendLine("- Do not use outside knowledge or invent citations.");
        return sb.ToString();
    }

    private static string BuildLocalDemoFallbackAnswer(string question, IReadOnlyList<AiChatSourceDto> sources)
    {
        var firstSource = sources[0];
        var excerpt = firstSource.Excerpt.Length <= 260
            ? firstSource.Excerpt
            : firstSource.Excerpt[..260].TrimEnd() + "...";

        return $"Local demo fallback answer: based on the retrieved source, the relevant information is: {excerpt} [{firstSource.Label}]";
    }

    private int NormalizeTopK(int topK)
    {
        var defaultTopK = _ragOptions.DefaultTopK > 0 ? _ragOptions.DefaultTopK : 5;
        var maxTopK = _ragOptions.MaxTopK > 0 ? _ragOptions.MaxTopK : 10;
        var requested = topK > 0 ? topK : defaultTopK;
        return Math.Clamp(requested, 1, maxTopK);
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 3)
        {
            return value[..Math.Min(value.Length, maxLength)];
        }

        return value.Length <= maxLength
            ? value
            : value[..(maxLength - 3)].TrimEnd() + "...";
    }
}
