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
    };

    private const int MaxRetries = 2;
    private const int MinQuestions = 3;
    private const int MaxQuestions = 12;
    private const int MaxQuestionsJsonBytes = 200 * 1024;

    private readonly AppDbContext _db;
    private readonly IRagSearchService _ragSearch;
    private readonly IAiChatCompletionClientFactory _clientFactory;
    private readonly GroqOptions _groqOptions;
    private readonly IChatPersistenceService _chatPersistence;
    private readonly ILogger<QuizService> _logger;

    public QuizService(
        AppDbContext db,
        IRagSearchService ragSearch,
        IAiChatCompletionClientFactory clientFactory,
        IOptions<GroqOptions> groqOptions,
        IChatPersistenceService chatPersistence,
        ILogger<QuizService> logger)
    {
        _db = db;
        _ragSearch = ragSearch;
        _clientFactory = clientFactory;
        _groqOptions = groqOptions.Value;
        _chatPersistence = chatPersistence;
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
        var supabaseUserIdStr = supabaseUserId.ToString();
        var previousQuestions = await _db.Quizzes
            .AsNoTracking()
            .Where(q => q.UserId == supabaseUserIdStr)
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
        var altModel = activeModel.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)
            ? "llama-3.3-70b-versatile"
            : "gemini-2.5-flash";

        string rawJson;
        Exception? fallbackError = null;

        // Try the requested model first
        try
        {
            var firstRequest = new AiChatCompletionRequest(systemPrompt, userPrompt, activeModel, MaxTokens: 8192);
            rawJson = await _clientFactory.GetClient(activeModel).CompleteAsync(firstRequest, ct);
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
                rawJson = await _clientFactory.GetClient(altModel).CompleteAsync(fallbackRequest, ct);
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
                    rawJson = await currentProvider.CompleteAsync(
                        new AiChatCompletionRequest(retryPrompt, userPrompt, activeModel, MaxTokens: 8192), ct);
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
                    activeModel = activeModel.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)
                        ? "llama-3.3-70b-versatile"
                        : "gemini-2.5-flash";
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
            UserId = supabaseUserId.ToString(),
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

    public async Task<QuizDto> ResumeAsync(Guid supabaseUserId, Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            var quiz = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.SessionId == sessionId && q.UserId == supabaseUserId.ToString())
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
            quiz = await _db.Quizzes
                .FirstOrDefaultAsync(q => q.Id == quizId && q.UserId == supabaseUserId.ToString(), ct)
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
            var quiz = await _db.Quizzes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == quizId && q.UserId == supabaseUserId.ToString(), ct);

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
}
