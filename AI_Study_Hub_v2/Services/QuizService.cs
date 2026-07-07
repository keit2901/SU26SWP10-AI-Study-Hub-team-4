using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class QuizService : IQuizService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const int MaxRetries = 2;
    private const int MinQuestions = 3;
    private const int MaxQuestions = 12;
    private const int MaxQuestionsJsonBytes = 200 * 1024;

    private const string DefaultGroqModel = "llama-3.3-70b-versatile";
    private const string DefaultGeminiModel = "gemini-2.5-flash";

    private readonly AppDbContext _db;
    private readonly IRagSearchService _ragSearch;
    private readonly IAiChatCompletionClientFactory _clientFactory;
    private readonly GroqOptions _groqOptions;
    private readonly IChatPersistenceService _chatPersistence;
    private readonly IAiQuotaService _quotaService;
    private readonly ILogger<QuizService> _logger;

    public QuizService(
        AppDbContext db,
        IRagSearchService ragSearch,
        IAiChatCompletionClientFactory clientFactory,
        IOptions<GroqOptions> groqOptions,
        IChatPersistenceService chatPersistence,
        IAiQuotaService quotaService,
        ILogger<QuizService> logger)
    {
        _db = db;
        _ragSearch = ragSearch;
        _clientFactory = clientFactory;
        _groqOptions = groqOptions.Value;
        _chatPersistence = chatPersistence;
        _quotaService = quotaService;
        _logger = logger;
    }

    public async Task<QuizDto> GenerateAsync(Guid supabaseUserId, GenerateQuizRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await _db.Users
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
            ?? throw new QuizException(404, "user_not_found",
                "User profile not found. Ensure your Supabase account is linked to a local profile.");

        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.UserId == profile.Id, ct)
            ?? throw new QuizException(404, "session_not_found", "Chat session not found.");

        var hasScope = request.DocumentIds is { Count: > 0 } || request.FolderId is not null || request.DocumentId is not null;
        if (!hasScope)
        {
            throw new QuizException(422, "missing_document_scope",
                "Please select at least one document to generate a quiz from.");
        }

        if (request.DocumentIds is { Count: > 0 })
        {
            var existingCount = await _db.Documents
                .CountAsync(d => request.DocumentIds.Contains(d.Id) && d.UserId == profile.Id, ct);
            if (existingCount != request.DocumentIds.Count)
            {
                throw new QuizException(404, "documents_not_found",
                    "One or more selected documents could not be found. They may have been deleted.");
            }
        }

        var count = Math.Clamp(request.Count, MinQuestions, MaxQuestions);
        var topK = Math.Clamp(count * 2, 5, 15);

        var ragRequest = new RagSearchRequest(
            Query: request.Title ?? "quiz generation context",
            DocumentId: request.DocumentId,
            FolderId: request.FolderId,
            SubjectCode: request.SubjectCode,
            Semester: request.Semester,
            TopK: topK,
            DocumentIds: request.DocumentIds,
            TopicKeyword: request.TopicKeyword);

        IReadOnlyList<RagSearchResultDto> searchResults;
        try
        {
            searchResults = await _ragSearch.SearchAsync(supabaseUserId, ragRequest, ct);
        }
        catch (DocumentException ex)
        {
            throw new QuizException(ex.StatusCode, ex.Code, ex.Message);
        }

        if (searchResults.Count == 0)
        {
            throw new QuizException(422, "insufficient_content",
                "The selected documents don't contain enough text to generate a meaningful quiz. Please select documents with more content.");
        }

        var context = BuildQuizContext(searchResults);

        // Load previous quiz questions to avoid duplicates
        var previousQuestions = await _db.Quizzes
            .AsNoTracking()
            .Where(q => q.UserId == profile.Id)
            .OrderByDescending(q => q.CreatedAt)
            .Take(20)
            .Select(q => q.QuestionsJson)
            .ToListAsync(ct);

        var existingQuestionTexts = new List<string>();
        foreach (var qJson in previousQuestions)
        {
            try
            {
                var prevQuiz = JsonSerializer.Deserialize<List<QuizQuestionParsed>>(qJson, JsonOptions);
                if (prevQuiz is not null)
                {
                    existingQuestionTexts.AddRange(prevQuiz.Select(q => q.Question));
                }
            }
            catch
            {
                // skip malformed quiz data
            }
        }

        if (existingQuestionTexts.Count > 0)
        {
            var exclusionNote = $"\n\n[EXCLUDED QUESTIONS — Do NOT generate these again]\n{string.Join("\n", existingQuestionTexts.Select(q => $"- {q}"))}\n";
            context += exclusionNote;
        }

        var systemPrompt = BuildQuizSystemPrompt(context, request.Difficulty, count);
        var userPrompt = $"Generate {count} quiz questions based on the source excerpts above. Difficulty: {request.Difficulty}.";

        var activeModel = request.Model;
        var altModel = activeModel?.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) == true
            ? DefaultGroqModel
            : DefaultGeminiModel;

        string rawJson;
        Exception? fallbackError = null;

        // Try the requested model first
        try
        {
            var firstRequest = new AiChatCompletionRequest(systemPrompt, userPrompt, activeModel, MaxTokens: 8192);
            rawJson = await CompleteWithQuotaAsync(
                supabaseUserId,
                _clientFactory.GetClient(activeModel),
                firstRequest,
                ct);
        }
        catch (AiChatProviderException ex)
        {
            _logger.LogWarning(ex, "AI provider {Model} failed. Falling back to {Fallback}.", activeModel, altModel);
            fallbackError = ex;
            await Task.Delay(1000, ct);
            try
            {
                activeModel = altModel;
                var fallbackRequest = new AiChatCompletionRequest(systemPrompt, userPrompt, altModel, MaxTokens: 8192);
                rawJson = await CompleteWithQuotaAsync(
                    supabaseUserId,
                    _clientFactory.GetClient(altModel),
                    fallbackRequest,
                    ct);
                fallbackError = null;
            }
            catch (AiChatProviderException ex2)
            {
                _logger.LogError(ex2, "Fallback AI provider {Fallback} also failed.", altModel);
                throw new QuizException(503, "ai_provider_unavailable",
                    "All AI providers failed. Please try again later.");
            }
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new QuizException(503, "empty_ai_response",
                "The AI returned an empty response. Please try again.");
        }

        var (parsed, failReason) = TryParseAndValidateQuizJson(rawJson, count);
        if (parsed is null)
        {
            _logger.LogWarning("AI returned invalid quiz JSON on first attempt (model: {Model}). Failure: {Reason}. Raw preview: {Preview}", activeModel, failReason, TruncatePreview(rawJson, 500));

            var currentProvider = _clientFactory.GetClient(activeModel);
            for (var attempt = 1; attempt <= MaxRetries; attempt++)
            {
                var retryPrompt = systemPrompt + "\n\nIMPORTANT: You MUST output ONLY valid JSON. No markdown, no code fences, no additional text. The JSON must match the schema exactly.";

                try
                {
                    await Task.Delay(attempt * 500, ct);
                    rawJson = await CompleteWithQuotaAsync(
                        supabaseUserId,
                        currentProvider,
                        new AiChatCompletionRequest(retryPrompt, userPrompt, activeModel, MaxTokens: 8192),
                        ct);
                    (parsed, failReason) = TryParseAndValidateQuizJson(rawJson, count);
                    if (parsed is not null)
                    {
                        break;
                    }

                    _logger.LogWarning("AI returned invalid quiz JSON on retry {Attempt} (model: {Model}). Failure: {Reason}. Raw preview: {Preview}", attempt, activeModel, failReason, TruncatePreview(rawJson, 500));
                }
                catch (AiChatProviderException ex)
                {
                    _logger.LogError(ex, "AI provider failed on retry {Attempt} (model: {Model}). Trying fallback model.", attempt, activeModel);
                    // Switch to the alternative model on provider failure
                    activeModel = activeModel?.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) == true
                        ? DefaultGroqModel
                        : DefaultGeminiModel;
                    currentProvider = _clientFactory.GetClient(activeModel);
                    _logger.LogInformation("Quiz retry switched to fallback model {Model}", activeModel);
                }
            }
        }

        if (parsed is null)
        {
            var preview = TruncatePreview(rawJson, 200);
            _logger.LogError("AI returned invalid quiz JSON after {MaxRetries} retries. Last failure: {Reason}. Raw preview: {Preview}", MaxRetries, failReason, preview);
            throw new QuizException(422, "invalid_quiz_json",
                $"Quiz generation failed: {failReason}. Raw response preview: {preview}");
        }

        var now = DateTimeOffset.UtcNow;
        var questionsJson = JsonSerializer.Serialize(parsed.Questions);
        if (Encoding.UTF8.GetByteCount(questionsJson) > MaxQuestionsJsonBytes)
        {
            throw new QuizException(413, "quiz_too_large",
                "Quiz content is too large. Try fewer questions or shorter documents.");
        }

        var quiz = new Quiz
        {
            Id = Guid.NewGuid(),
            SessionId = request.SessionId,
            UserId = profile.Id,
            Title = parsed.Title,
            Status = QuizStatus.InProgress,
            CurrentQuestionIndex = 0,
            TotalQuestions = parsed.Questions.Count,
            QuestionsJson = questionsJson,
            Score = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Quizzes.Add(quiz);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist quiz {QuizId} to database.", quiz.Id);
            throw new QuizException(500, "quiz_save_failed",
                "Could not save quiz. Please try again.");
        }

        var scopeLabel = request.ScopeLabel ?? BuildScopeLabel(request);
        var userContent = $"Generate quiz: {parsed.Title}";
        try
        {
            await _chatPersistence.SaveQuizExchangeAsync(
                supabaseUserId,
                request.SessionId,
                scopeLabel,
                userContent,
                quiz.Id,
                parsed.Title,
                "InProgress",
                totalQuestions: parsed.Questions.Count,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist quiz exchange to chat history for quiz {QuizId}.", quiz.Id);
        }

        return MapToDto(quiz);
    }

    private async Task<string> CompleteWithQuotaAsync(
        Guid supabaseUserId,
        IAiChatCompletionClient client,
        AiChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        AiQuotaReservation reservation;
        try
        {
            reservation = await _quotaService.ReserveAsync(
                supabaseUserId,
                EstimateTokens(request.SystemPrompt, request.UserPrompt) + 1_024,
                cancellationToken);
        }
        catch (AiQuotaException ex)
        {
            throw new QuizException(ex.StatusCode, ex.Code, ex.Message);
        }

        var completed = false;
        try
        {
            var response = await client.CompleteAsync(request, cancellationToken);
            await _quotaService.CompleteAsync(
                reservation,
                EstimateTokens(request.SystemPrompt, request.UserPrompt, response),
                cancellationToken);
            completed = true;
            return response;
        }
        finally
        {
            if (!completed)
            {
                try
                {
                    await _quotaService.ReleaseAsync(reservation, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to release quiz AI quota reservation for user {UserId}.", supabaseUserId);
                }
            }
        }
    }

    private static int EstimateTokens(params string[] values)
    {
        var characters = values.Sum(value => value?.Length ?? 0);
        return Math.Max(1, (characters + 3) / 4);
    }

    public async Task<QuizDto> ResumeAsync(Guid supabaseUserId, Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            var profile = await _db.Users
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
                ?? throw new QuizException(404, "user_not_found", "User profile not found.");

            var quiz = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.SessionId == sessionId && q.UserId == profile.Id)
                .OrderByDescending(q => q.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (quiz is null)
            {
                throw new QuizException(404, "no_active_quiz",
                    "No active quiz found for this session.");
            }

            return MapToDto(quiz);
        }
        catch (QuizException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load quiz for session {SessionId}.", sessionId);
            throw new QuizException(500, "quiz_load_failed",
                "Could not load quiz. Please try again.");
        }
    }

    public async Task SaveAsync(Guid supabaseUserId, Guid quizId, SaveQuizRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Quiz quiz;
        try
        {
            var profile = await _db.Users
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
                ?? throw new QuizException(404, "user_not_found", "User profile not found.");

            quiz = await _db.Quizzes
                .FirstOrDefaultAsync(q => q.Id == quizId && q.UserId == profile.Id, ct)
                ?? throw new QuizException(404, "quiz_not_found", "Quiz not found.");
        }
        catch (QuizException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find quiz {QuizId} for save.", quizId);
            throw new QuizException(500, "quiz_load_failed",
                "Could not load quiz for saving. Please try again.");
        }

        quiz.Status = request.Status;
        quiz.CurrentQuestionIndex = request.CurrentQuestionIndex;
        quiz.AnswersJson = JsonSerializer.Serialize(request.Answers);
        quiz.SubmittedJson = JsonSerializer.Serialize(request.Submitted);
        quiz.Score = request.Score;
        quiz.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save quiz {QuizId}.", quizId);
            throw new QuizException(500, "quiz_save_failed",
                "Could not save quiz progress. Please try again.");
        }

        var statusStr = request.Status switch
        {
            QuizStatus.Completed => "Completed",
            QuizStatus.InProgress => "InProgress",
            _ => "InProgress",
        };
        try
        {
            await _chatPersistence.UpdateQuizMetadataAsync(
                supabaseUserId,
                quizId,
                statusStr,
                totalQuestions: quiz.TotalQuestions,
                score: request.Score,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update quiz metadata for quiz {QuizId}", quizId);
        }
    }

    public async Task<QuizDto?> GetByIdAsync(Guid supabaseUserId, Guid quizId, CancellationToken ct = default)
    {
        try
        {
            var profile = await _db.Users
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
                ?? throw new QuizException(404, "user_not_found", "User profile not found.");

            var quiz = await _db.Quizzes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == quizId && q.UserId == profile.Id, ct);

            if (quiz is null)
            {
                throw new QuizException(404, "quiz_not_found", "Quiz not found.");
            }

            return MapToDto(quiz);
        }
        catch (QuizException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load quiz {QuizId}.", quizId);
            throw new QuizException(500, "quiz_load_failed",
                "Could not load quiz. Please try again.");
        }
    }

    private static string BuildQuizContext(IReadOnlyList<RagSearchResultDto> results)
    {
        var sb = new StringBuilder();
        foreach (var result in results)
        {
            sb.Append('[').Append(result.SourceLabel).Append("] ");
            sb.Append(result.FileName);
            if (result.PageNumber.HasValue)
            {
                sb.Append(", page ").Append(result.PageNumber.Value);
            }
            sb.Append(", chunk ").Append(result.ChunkIndex).AppendLine();
            sb.AppendLine(result.ContentExcerpt);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildQuizSystemPrompt(string context, string difficulty, int count)
    {
        var diffLower = difficulty?.ToLowerInvariant() switch
        {
            "easy" => "EASY — test basic recall of key terms and definitions.",
            "hard" => "HARD — test deep understanding and application of concepts.",
            _ => "MEDIUM — test understanding, not just recall. Include some application questions.",
        };

        var header = $"""
You are AI Study Hub, an AI quiz generator for student study materials.
Current date: {DateTimeOffset.UtcNow:yyyy-MM-dd}.

[SOURCE EXCERPTS]
{context}

Generate a quiz based ONLY on the source excerpts above.
Do not use outside knowledge.
The quiz must be at {diffLower}

Rules:
1. Output VALID JSON only — no markdown, no code fences, no additional text.
2. Each question must have exactly 4 options (A, B, C, D).
3. Only one option must be correct.
4. Include an explanation for the correct answer.
5. If a source excerpt is used, include its source label like "S1" as the sourceLabel.
6. Questions must be factual and grounded in the provided excerpts.
7. The [EXCLUDED QUESTIONS] section lists questions that have already been asked in previous quizzes. Do NOT generate any question that matches or is substantially similar to any of those excluded questions.

Output exactly this JSON structure (no extra text):
""";

        var jsonTemplate = """
{
  "title": "Quiz title based on content",
  "questions": [
    {
      "question": "Question text here?",
      "subtitle": "Optional subtitle or context.",
      "options": [
        {"id": "A", "text": "First option"},
        {"id": "B", "text": "Second option"},
        {"id": "C", "text": "Third option"},
        {"id": "D", "text": "Fourth option"}
      ],
      "correctOptionId": "B",
      "explanation": "Why the correct answer is right and the others are wrong.",
      "sourceLabel": "S1"
    }
  ]
}
""";

        return header + jsonTemplate;
    }

    private static (QuizJsonParsed? Parsed, string? FailureReason) TryParseAndValidateQuizJson(string raw, int expectedCount)
    {
        var cleaned = StripJsonFences(raw);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return (null, "AI response was empty after stripping markdown fences.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(cleaned);
        }
        catch (JsonException ex)
        {
            return (null, $"AI response is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("title", out var titleProp) || titleProp.ValueKind != JsonValueKind.String)
            {
                return (null, "JSON is missing a valid 'title' string field.");
            }

            if (!root.TryGetProperty("questions", out var questionsProp) || questionsProp.ValueKind != JsonValueKind.Array)
            {
                return (null, "JSON is missing a valid 'questions' array field.");
            }

            var questions = new List<QuizQuestionParsed>();
            var index = 0;
            var skippedDueOptions = 0;
            var skippedDueNoQuestion = 0;
            var skippedDueCorrectId = 0;
            foreach (var q in questionsProp.EnumerateArray())
            {
                if (!q.TryGetProperty("question", out var qProp) || qProp.ValueKind != JsonValueKind.String)
                {
                    skippedDueNoQuestion++;
                    continue;
                }

                if (!q.TryGetProperty("options", out var optsProp) || optsProp.ValueKind != JsonValueKind.Array)
                {
                    skippedDueNoQuestion++;
                    continue;
                }

                var options = new List<QuizOptionDto>();
                foreach (var o in optsProp.EnumerateArray())
                {
                    var id = o.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    var text = o.TryGetProperty("text", out var txtProp) ? txtProp.GetString() : null;
                    if (id is null || text is null) continue;
                    options.Add(new QuizOptionDto(id, text));
                }

                if (options.Count != 4)
                {
                    skippedDueOptions++;
                    continue;
                }

                var correctId = q.TryGetProperty("correctOptionId", out var cId) ? cId.GetString() : null;
                if (correctId is null || !options.Any(o => o.Id == correctId))
                {
                    skippedDueCorrectId++;
                    continue;
                }

                var explanation = q.TryGetProperty("explanation", out var exp) ? exp.GetString() ?? "" : "";
                var subtitle = q.TryGetProperty("subtitle", out var sub) ? sub.GetString() ?? "" : "";
                var sourceLabel = q.TryGetProperty("sourceLabel", out var src) ? src.GetString() : null;

                questions.Add(new QuizQuestionParsed(
                    index++,
                    qProp.GetString()!,
                    subtitle,
                    options.AsReadOnly(),
                    correctId,
                    explanation,
                    sourceLabel));
            }

            if (questions.Count < MinQuestions)
            {
                var totalQs = questionsProp.GetArrayLength();
                return (null, $"Only {questions.Count} valid question(s) parsed (need {MinQuestions}). Total entries in array: {totalQs}. " +
                    $"Skipped: {skippedDueNoQuestion} missing question/options, {skippedDueOptions} wrong option count, {skippedDueCorrectId} invalid correctOptionId.");
            }

            return (new QuizJsonParsed(
                titleProp.GetString()!,
                questions.AsReadOnly()), null);
        }
    }

    private static string TruncatePreview(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(empty)";
        }

        return value.Length <= maxLen ? value : value[..maxLen] + "...";
    }

    private static string StripJsonFences(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```"))
        {
            var start = trimmed.IndexOf('\n');
            if (start > 0)
            {
                trimmed = trimmed[(start + 1)..];
            }

            var end = trimmed.LastIndexOf("```");
            if (end >= 0)
            {
                trimmed = trimmed[..end];
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            trimmed = trimmed[firstBrace..(lastBrace + 1)];
        }

        return trimmed.Trim();
    }

    private static string BuildScopeLabel(GenerateQuizRequest request)
    {
        if (request.DocumentIds is { Count: > 0 })
        {
            return $"{request.DocumentIds.Count} selected file{(request.DocumentIds.Count > 1 ? "s" : "")}";
        }

        if (request.FolderId is not null)
        {
            return "Selected folder";
        }

        if (request.DocumentId is not null)
        {
            return "Selected document";
        }

        return "Selected documents";
    }

    private static QuizDto MapToDto(Quiz quiz)
    {
        var questions = string.IsNullOrWhiteSpace(quiz.QuestionsJson)
            ? new List<QuizQuestionDto>()
            : JsonSerializer.Deserialize<List<QuizQuestionParsed>>(quiz.QuestionsJson, JsonOptions)?
                .Select(q => new QuizQuestionDto(q.Index, q.Question, q.Subtitle, q.Options, q.CorrectOptionId, q.Explanation, q.SourceLabel))
                .ToList() ?? new List<QuizQuestionDto>();

        var answers = string.IsNullOrWhiteSpace(quiz.AnswersJson)
            ? new Dictionary<int, string?>()
            : JsonSerializer.Deserialize<Dictionary<int, string?>>(quiz.AnswersJson, JsonOptions) ?? new();

        var submitted = string.IsNullOrWhiteSpace(quiz.SubmittedJson)
            ? new Dictionary<int, bool>()
            : JsonSerializer.Deserialize<Dictionary<int, bool>>(quiz.SubmittedJson, JsonOptions) ?? new();

        return new QuizDto(
            quiz.Id,
            quiz.Title,
            quiz.Status,
            quiz.CurrentQuestionIndex,
            quiz.TotalQuestions,
            questions.AsReadOnly(),
            answers.AsReadOnly()!,
            submitted.AsReadOnly()!,
            quiz.Score,
            quiz.CreatedAt);
    }

    private sealed record QuizJsonParsed(
        string Title,
        IReadOnlyList<QuizQuestionParsed> Questions);

    private sealed record QuizQuestionParsed(
        int Index,
        string Question,
        string Subtitle,
        IReadOnlyList<QuizOptionDto> Options,
        string CorrectOptionId,
        string Explanation,
        string? SourceLabel);

    // Sprint 3: Standalone quiz APIs ------------------------------------------------

    public async Task<QuizGenerateResponse> GenerateAsyncV2(
        Guid supabaseUserId,
        QuizGenerateRequestV2 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await ResolveActiveUserAsync(supabaseUserId, cancellationToken);
        var prompt = NormalizePrompt(request.Prompt);
        var topK = request.TopK > 0 ? request.TopK : 5;

        IReadOnlyList<RagSearchResultDto> sources;
        try
        {
            sources = await _ragSearch.SearchAsync(
                supabaseUserId,
                new RagSearchRequest(
                    prompt,
                    request.DocumentId,
                    request.FolderId,
                    NormalizeFilter(request.SubjectCode),
                    NormalizeFilter(request.Semester),
                    topK,
                    request.DocumentIds),
                cancellationToken);
        }
        catch (DocumentException ex)
        {
            throw new QuizException(ex.StatusCode, ex.Code, ex.Message);
        }

        var usableSources = sources
            .Where(s => !string.IsNullOrWhiteSpace(s.ContentExcerpt))
            .Take(Math.Max(1, topK))
            .ToList();

        if (usableSources.Count == 0)
        {
            throw new QuizException(404, "no_quiz_sources", "No indexed document sources were found for quiz generation.");
        }

        var requestedCount = request.QuestionCount > 0 ? request.QuestionCount : 3;
        var questionCount = Math.Clamp(requestedCount, 1, Math.Min(10, usableSources.Count));
        var storedQuestions = BuildQuestions(prompt, usableSources, questionCount);
        var sourceDtos = usableSources.Select((source, index) => ToQuizSource(source, index)).ToList();
        var now = DateTimeOffset.UtcNow;
        var title = BuildTitle(prompt);

        var quiz = new Quiz
        {
            Id = Guid.NewGuid(),
            SessionId = Guid.Empty,
            UserId = user.Id,
            Title = title,
            Status = QuizStatus.Completed,
            TotalQuestions = storedQuestions.Count,
            QuestionsJson = JsonSerializer.Serialize(storedQuestions, JsonOptions),
            ScopeJson = JsonSerializer.Serialize(new
            {
                prompt,
                request.DocumentId,
                request.FolderId,
                request.DocumentIds,
                subjectCode = NormalizeFilter(request.SubjectCode),
                semester = NormalizeFilter(request.Semester),
                topK,
                sources = sourceDtos,
            }, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Quizzes.Add(quiz);
        await _db.SaveChangesAsync(cancellationToken);

        return new QuizGenerateResponse(
            quiz.Id,
            quiz.Title,
            storedQuestions.Select(ToPublicQuestion).ToList(),
            sourceDtos,
            quiz.CreatedAt);
    }

    public async Task<QuizSubmitResponse> SubmitAsync(
        Guid supabaseUserId,
        Guid quizId,
        QuizSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await ResolveActiveUserAsync(supabaseUserId, cancellationToken);
        if (quizId == Guid.Empty)
        {
            throw new QuizException(400, "quiz_id_required", "Quiz id is required.");
        }

        var quiz = await _db.Quizzes
            .FirstOrDefaultAsync(q => q.Id == quizId && q.UserId == user.Id, cancellationToken)
            ?? throw new QuizException(404, "quiz_not_found", "Quiz was not found for the authenticated user.");

        var questions = JsonSerializer.Deserialize<List<StoredQuizQuestion>>(quiz.QuestionsJson, JsonOptions) ?? new();
        if (questions.Count == 0)
        {
            throw new QuizException(409, "quiz_has_no_questions", "Quiz has no questions to grade.");
        }

        var answerMap = (request.Answers ?? Array.Empty<QuizAnswerDto>())
            .Where(a => !string.IsNullOrWhiteSpace(a.QuestionId))
            .GroupBy(a => a.QuestionId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().OptionId?.Trim(), StringComparer.OrdinalIgnoreCase);

        var results = new List<QuizQuestionResultDto>(questions.Count);
        var score = 0;
        foreach (var question in questions)
        {
            answerMap.TryGetValue(question.Id, out var submittedOptionId);
            var isCorrect = string.Equals(submittedOptionId, question.CorrectOptionId, StringComparison.OrdinalIgnoreCase);
            if (isCorrect)
            {
                score++;
            }

            results.Add(new QuizQuestionResultDto(
                question.Id,
                isCorrect,
                string.IsNullOrWhiteSpace(submittedOptionId) ? null : submittedOptionId,
                question.CorrectOptionId,
                question.Explanation));
        }

        var now = DateTimeOffset.UtcNow;
        quiz.Score = score;
        quiz.Status = QuizStatus.Completed;
        quiz.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        return new QuizSubmitResponse(
            Guid.NewGuid(), quiz.Id, score, questions.Count, results, now);
    }

    private async Task<User> ResolveActiveUserAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        if (supabaseUserId == Guid.Empty)
        {
            throw new QuizException(401, "missing_user_id", "Authenticated Supabase user id is missing.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new QuizException(404, "user_not_found", "Authenticated user has no profile in public.users.");

        if (!user.IsActive)
        {
            throw new QuizException(403, "user_inactive", "User account is inactive.");
        }

        return user;
    }

    private static string NormalizePrompt(string? prompt)
    {
        var normalized = string.Join(' ', (prompt ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length < 3)
        {
            throw new QuizException(400, "prompt_required", "Quiz prompt or topic is required.");
        }

        return normalized;
    }

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string BuildTitle(string prompt) => $"Quiz: {Shorten(prompt, 80)}";

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private static IReadOnlyList<StoredQuizQuestion> BuildQuestions(
        string prompt,
        IReadOnlyList<RagSearchResultDto> sources,
        int questionCount)
    {
        var questions = new List<StoredQuizQuestion>(questionCount);
        for (var i = 0; i < questionCount; i++)
        {
            var source = sources[i % sources.Count];
            var sentence = FirstSentence(source.ContentExcerpt);
            var keyPhrase = ExtractKeyPhrase(sentence, prompt);
            var label = string.IsNullOrWhiteSpace(source.SourceLabel) ? $"S{i + 1}" : source.SourceLabel;

            questions.Add(new StoredQuizQuestion(
                $"q{i + 1}",
                $"According to {label}, which statement best answers: {Shorten(prompt, 90)}?",
                new[]
                {
                    new QuizOptionDto("A", $"It is unrelated to {Shorten(prompt, 48)}."),
                    new QuizOptionDto("B", keyPhrase),
                    new QuizOptionDto("C", "It is not mentioned in the retrieved source."),
                    new QuizOptionDto("D", "It only applies when no documents are indexed."),
                },
                "B",
                $"The answer is grounded in {label}: {sentence}",
                label));
        }

        return questions;
    }

    private static QuizQuestionDtoV2 ToPublicQuestion(StoredQuizQuestion question) =>
        new(question.Id, question.Text, question.Options, question.SourceLabel, Explanation: null);

    private static QuizSourceDto ToQuizSource(RagSearchResultDto source, int index) =>
        new(
            string.IsNullOrWhiteSpace(source.SourceLabel) ? $"S{index + 1}" : source.SourceLabel,
            source.DocumentId,
            source.FileName,
            source.ChunkIndex,
            source.PageNumber,
            source.Score);

    private static string FirstSentence(string excerpt)
    {
        var normalized = string.Join(' ', (excerpt ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "The retrieved source contains relevant study context.";
        }

        var terminator = normalized.IndexOfAny(new[] { '.', '!', '?' });
        var sentence = terminator >= 24 ? normalized[..(terminator + 1)] : normalized;
        return Shorten(sentence, 220);
    }

    private static string ExtractKeyPhrase(string sentence, string prompt)
    {
        var candidate = sentence.Length >= 18 ? sentence : $"The source supports the topic: {prompt}.";
        return Shorten(candidate, 140);
    }

    private sealed record StoredQuizQuestion(
        string Id,
        string Text,
        IReadOnlyList<QuizOptionDto> Options,
        string CorrectOptionId,
        string Explanation,
        string? SourceLabel);
}
