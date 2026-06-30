using System.Diagnostics;
using System.Text;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class SemanticKernelRagChatService : IAiChatService
{
    private static string BuildSystemPrompt() =>
        "You are AI Study Hub, a tutoring study assistant. " +
        $"Current date: {DateTimeOffset.UtcNow:yyyy-MM-dd}. " +
        "Your training data covers events and facts up to December 2023. You can confidently answer questions about people, events, and facts from 2023 and earlier. " +
        "If the question asks about events after December 2023 that you have no source context for, clearly say your training data does not cover that period. " +
        "Below you will find a student question and optionally some source excerpts from their documents. " +
        "If source excerpts are provided, answer using ONLY those excerpts and cite every factual claim " +
        "with source markers like [S1]. Do not invent citations — only cite what's in the provided excerpts. " +
        "If the excerpts do not contain enough information, say the indexed documents do not contain enough information. " +
        "If no source excerpts are provided, answer using your general knowledge (which covers up to December 2023). " +
        "Adopt a tutorial tone: explain concepts clearly, use examples when helpful, and guide the student toward understanding. " +
        "Format answers with bold key terms, bullet points for lists, and clear structure. " +
        "Keep the answer concise, accurate, and study-friendly.";

    private static string BuildGeneralSystemPrompt() =>
        "You are AI Study Hub, a tutoring study assistant. " +
        $"Current date: {DateTimeOffset.UtcNow:yyyy-MM-dd}. " +
        "Your training data covers events and facts up to December 2023. You can confidently answer questions about people, events, and facts from 2023 and earlier. " +
        "If the question asks about events after December 2023, clearly say your training data does not cover that period. " +
        "Answer using your general knowledge. Adopt a tutorial tone: explain concepts clearly, use examples when helpful, and guide the student toward understanding. " +
        "Format answers with bold key terms, bullet points for lists, and clear structure. " +
        "Keep the answer concise, accurate, and study-friendly.";

    private readonly IRagSearchService _ragSearchService;
    private readonly IAiChatCompletionClientFactory _clientFactory;
    private readonly RagOptions _ragOptions;
    private readonly GroqOptions _groqOptions;
    private readonly IAiQuotaService _quotaService;
    private readonly ILogger<SemanticKernelRagChatService> _logger;

    public SemanticKernelRagChatService(
        IRagSearchService ragSearchService,
        IAiChatCompletionClientFactory clientFactory,
        IOptions<RagOptions> ragOptions,
        IOptions<GroqOptions> groqOptions,
        IAiQuotaService quotaService,
        ILogger<SemanticKernelRagChatService> logger)
    {
        _ragSearchService = ragSearchService;
        _clientFactory = clientFactory;
        _ragOptions = ragOptions.Value;
        _groqOptions = groqOptions.Value;
        _quotaService = quotaService;
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

        var hasDocumentScope = request.DocumentId is not null
            || request.DocumentIds is { Count: > 0 };

        IReadOnlyList<RagSearchResultDto> searchResults;
        if (hasDocumentScope)
        {
            var ragRequest = new RagSearchRequest(
                question,
                request.DocumentId,
                request.FolderId,
                NormalizeFilter(request.SubjectCode),
                NormalizeFilter(request.Semester),
                NormalizeTopK(request.TopK),
                request.DocumentIds);

            try
            {
                searchResults = await _ragSearchService.SearchAsync(supabaseUserId, ragRequest, cancellationToken);
            }
            catch (DocumentException ex)
            {
                throw new AiChatException(ex.StatusCode, ex.Code, ex.Message);
            }
        }
        else
        {
            searchResults = Array.Empty<RagSearchResultDto>();
        }

        var sources = MapSources(searchResults);
        var hadDocumentSelection = hasDocumentScope;
        var userPrompt = BuildUserPrompt(question, sources, request.ChatHistory, hadDocumentSelection);
        var systemPrompt = hasDocumentScope ? BuildSystemPrompt() : BuildGeneralSystemPrompt();
        var completionRequest = new AiChatCompletionRequest(systemPrompt, userPrompt, request.Model);
        var client = _clientFactory.GetClient(request.Model);

        AiQuotaReservation reservation;
        try
        {
            var estimatedTokens = EstimateTokens(systemPrompt, userPrompt) + 512;
            reservation = await _quotaService.ReserveAsync(
                supabaseUserId,
                estimatedTokens,
                cancellationToken);
        }
        catch (AiQuotaException ex)
        {
            throw new AiChatException(ex.StatusCode, ex.Code, ex.Message);
        }

        var quotaCompleted = false;
        try
        {
            string answer;
            try
            {
                answer = await client.CompleteAsync(completionRequest, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (_groqOptions.UseLocalDemoFallback && sources.Count > 0)
                {
                    _logger.LogWarning(ex,
                        "AI chat provider failed; using local demo fallback answer because Groq:UseLocalDemoFallback is enabled.");
                    answer = BuildLocalDemoFallbackAnswer(question, sources);
                }
                else
                {
                    _logger.LogError(ex, "AI chat provider failed while answering question.");
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

            await _quotaService.CompleteAsync(
                reservation,
                EstimateTokens(systemPrompt, userPrompt, answer),
                cancellationToken);
            quotaCompleted = true;

            var hasSources = sources.Count > 0;
            return new AiChatAnswerResponse(
                answer.Trim(),
                hasSources ? sources : Array.Empty<AiChatSourceDto>(),
                RefusalReason: null,
                DurationMs: stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            if (!quotaCompleted)
            {
                try
                {
                    await _quotaService.ReleaseAsync(reservation, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to release AI quota reservation for user {UserId}.", supabaseUserId);
                }
            }
        }
    }

    private static int EstimateTokens(params string[] values)
    {
        var characters = values.Sum(value => value?.Length ?? 0);
        return Math.Max(1, (characters + 3) / 4);
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

    private const int MaxHistoryExchanges = 5;
    private const int MaxAssistantAnswerChars = 300;

    private static string BuildChatHistorySection(IReadOnlyList<ChatMessageDto>? history)
    {
        if (history is null or { Count: 0 })
        {
            return string.Empty;
        }

        // Take only the most recent exchanges (last MaxHistoryExchanges * 2 messages)
        var recentMessages = history
            .OrderBy(m => m.SequenceNumber)
            .TakeLast(MaxHistoryExchanges * 2)
            .ToList();

        if (recentMessages.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Previous conversation");
        foreach (var msg in recentMessages)
        {
            var role = msg.Role == "user" ? "User" : "Assistant";
            var content = msg.Role == "assistant" && msg.Content.Length > MaxAssistantAnswerChars
                ? Truncate(msg.Content, MaxAssistantAnswerChars)
                : msg.Content;
            sb.Append(role).Append(": ").AppendLine(content);
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildUserPrompt(
        string question,
        IReadOnlyList<AiChatSourceDto> sources,
        IReadOnlyList<ChatMessageDto>? chatHistory = null,
        bool hadDocumentSelection = false)
    {
        var sb = new StringBuilder();

        // Prepend chat history so AI has multi-turn conversation context
        if (chatHistory is { Count: > 0 })
        {
            sb.Append(BuildChatHistorySection(chatHistory));
        }

        sb.AppendLine("## Student question");
        sb.AppendLine(question);
        sb.AppendLine();

        if (sources.Count == 0)
        {
            sb.AppendLine("## Source excerpts");
            if (hadDocumentSelection)
            {
                sb.AppendLine("A document scope was specified but no relevant excerpts were found. Answer using your general knowledge without citations.");
            }
            else
            {
                sb.AppendLine("No source excerpts are available for this question. Answer using your general knowledge without citations.");
            }
            return sb.ToString();
        }

        sb.AppendLine("## Source excerpts");

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

        sb.AppendLine("## Instructions");
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
