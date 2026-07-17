using System.Diagnostics;
using System.Data;
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

    private const int MinQuestions = 3;
    private const int MaxQuestions = 12;
    private const int MaxQuestionsJsonBytes = 200 * 1024;
    private const int MaxPreviousQuizzes = 5;
    private const int MaxPreviousQuestions = 40;
    private const int MaxExclusionPromptChars = 4000;

    private readonly AppDbContext _db;
    private readonly IRagSearchService _ragSearch;
    private readonly IAiChatCompletionClientFactory _clientFactory;
    private readonly GroqOptions _groqOptions;
    private readonly GeminiOptions _geminiOptions;
    private readonly IChatPersistenceService _chatPersistence;
    private readonly IAiQuotaService _quotaService;
    private readonly ILogger<QuizService> _logger;

    public QuizService(
        AppDbContext db,
        IRagSearchService ragSearch,
        IAiChatCompletionClientFactory clientFactory,
        IOptions<GroqOptions> groqOptions,
        IOptions<GeminiOptions> geminiOptions,
        IChatPersistenceService chatPersistence,
        IAiQuotaService quotaService,
        ILogger<QuizService> logger)
    {
        _db = db;
        _ragSearch = ragSearch;
        _clientFactory = clientFactory;
        _groqOptions = groqOptions.Value;
        _geminiOptions = geminiOptions.Value;
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

        if (session.FolderId != request.FolderId)
        {
            throw new QuizException(404, "session_not_found", "Chat session not found.");
        }

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

        var scopeJson = BuildCanonicalScopeJson(request);
        var previousQuestions = await _db.Quizzes
            .AsNoTracking()
            .Where(q => q.UserId == profile.Id && q.ScopeJson == scopeJson && q.Status != QuizStatus.GeneratingFailed)
            .OrderByDescending(q => q.CreatedAt)
            .ThenByDescending(q => q.Id)
            .Take(MaxPreviousQuizzes)
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
                    if (existingQuestionTexts.Count >= MaxPreviousQuestions)
                    {
                        existingQuestionTexts.RemoveRange(MaxPreviousQuestions, existingQuestionTexts.Count - MaxPreviousQuestions);
                        break;
                    }
                }
            }
            catch
            {
                // skip malformed quiz data
            }
        }

        var previousQuestionKeys = existingQuestionTexts
            .Select(NormalizeQuestionForDuplicateComparison)
            .Where(question => question.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        context += BuildExclusionNote(existingQuestionTexts);

        var systemPrompt = BuildQuizSystemPrompt(context, request.Difficulty, count);
        var userPrompt = $"Generate {count} quiz questions based on the source excerpts above. Difficulty: {request.Difficulty}.";

        var activeModel = string.IsNullOrWhiteSpace(request.Model) ? _groqOptions.Model : request.Model.Trim();
        var alternateModel = GetAlternateModel(activeModel);
        var alternateUsed = false;
        string rawJson;

        // Provider clients own the bounded transport retry. This service makes only logical
        // content/fallback calls: primary, then one alternate on terminal provider failure.
        try
        {
            var firstRequest = new AiChatCompletionRequest(systemPrompt, userPrompt, activeModel, MaxTokens: 8192);
            rawJson = await CompleteWithQuotaAsync(
                supabaseUserId,
                _clientFactory.GetClient(activeModel),
                firstRequest,
                ct);
        }
        catch (AiChatModelException ex)
        {
            throw new QuizException(400, ex.Code, ex.Message);
        }
        catch (AiChatProviderException ex)
        {
            if (alternateModel is null)
            {
                throw new QuizException(503, "ai_provider_unavailable", "The configured AI provider is unavailable. Please try again later.");
            }

            _logger.LogWarning(ex, "Primary AI provider {Model} failed; using configured alternate.", activeModel);
            activeModel = alternateModel;
            alternateUsed = true;
            try
            {
                var fallbackRequest = new AiChatCompletionRequest(systemPrompt, userPrompt, activeModel, MaxTokens: 8192);
                rawJson = await CompleteWithQuotaAsync(
                    supabaseUserId,
                    _clientFactory.GetClient(activeModel),
                    fallbackRequest,
                    ct);
            }
            catch (AiChatProviderException ex2)
            {
                _logger.LogWarning(ex2, "Configured alternate AI provider {Model} failed.", activeModel);
                throw new QuizException(503, "ai_provider_unavailable",
                    "All AI providers failed. Please try again later.");
            }
        }

        var (parsed, failReason) = TryParseAndValidateQuizJson(rawJson, count, previousQuestionKeys);
        if (parsed is null)
        {
            _logger.LogWarning("AI returned invalid quiz content from model {Model}. Failure: {Reason}.", activeModel, failReason);
            var repairPrompt = systemPrompt + $"\n\nIMPORTANT: Repair the previous response because: {failReason} Output ONLY valid JSON. No markdown, no code fences, no additional text. The JSON must match the schema exactly.";

            try
            {
                rawJson = await CompleteWithQuotaAsync(
                    supabaseUserId,
                    _clientFactory.GetClient(activeModel),
                    new AiChatCompletionRequest(repairPrompt, userPrompt, activeModel, MaxTokens: 8192),
                    ct);
            }
            catch (AiChatProviderException ex) when (!alternateUsed && alternateModel is not null)
            {
                activeModel = alternateModel;
                alternateUsed = true;
                _logger.LogWarning(ex, "Quiz repair provider failed; attempting one repair with configured alternate {Model}.", activeModel);
                try
                {
                    rawJson = await CompleteWithQuotaAsync(
                        supabaseUserId,
                        _clientFactory.GetClient(activeModel),
                        new AiChatCompletionRequest(repairPrompt, userPrompt, activeModel, MaxTokens: 8192),
                        ct);
                }
                catch (AiChatProviderException alternateEx)
                {
                    _logger.LogWarning(alternateEx, "Configured alternate AI provider failed during quiz repair.");
                    throw new QuizException(503, "ai_provider_unavailable", "All AI providers failed. Please try again later.");
                }
            }
            catch (AiChatProviderException ex)
            {
                _logger.LogWarning(ex, "AI provider failed during quiz repair.");
                throw new QuizException(503, "ai_provider_unavailable", "The AI provider is unavailable. Please try again later.");
            }

            (parsed, failReason) = TryParseAndValidateQuizJson(rawJson, count, previousQuestionKeys);
        }

        if (parsed is null)
        {
            _logger.LogWarning("AI returned invalid quiz content after its single repair allowance. Failure: {Reason}.", failReason);
            throw new QuizException(422, "invalid_quiz_json",
                $"Quiz generation failed: {failReason}.");
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
            ScopeJson = scopeJson,
            Score = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Revalidate at the persistence boundary so a workspace/session change during generation cannot save a quiz into another scope.
        var sessionStillInScope = await _db.ChatSessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == request.SessionId && s.UserId == profile.Id && s.FolderId == request.FolderId, ct);
        if (!sessionStillInScope)
        {
            throw new QuizException(404, "session_not_found", "Chat session not found.");
        }

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
                request.FolderId,
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

    public async Task<QuizDto> SaveAsync(Guid supabaseUserId, Guid quizId, SaveQuizRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        User profile;
        try
        {
            profile = await _db.Users
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
                ?? throw new QuizException(404, "user_not_found", "User profile not found.");
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

        var quiz = _db.Database.IsRelational()
            ? await SaveRelationalAsync(profile.Id, quizId, request, ct)
            : await SaveNonRelationalAsync(profile.Id, quizId, request, ct);

        var statusStr = quiz.Status switch
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
                score: quiz.Score,
                ct: ct);
        }

        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update quiz metadata for quiz {QuizId}", quizId);
        }

        return MapToDto(quiz);
    }

    private async Task<Quiz> SaveRelationalAsync(Guid profileId, Guid quizId, SaveQuizRequest request, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // The mapped table and ownership column are public.quizzes and user_id. Reloading
            // through this lock makes immutable answer validation observe the committed winner.
            var quiz = await _db.Quizzes
                .FromSqlRaw("SELECT * FROM quizzes WHERE id = {0} AND user_id = {1} FOR UPDATE", quizId, profileId)
                .SingleOrDefaultAsync(ct)
                ?? throw new QuizException(404, "quiz_not_found", "Quiz not found.");

            ApplySaveMutation(quiz, request);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return quiz;
        }
        catch (OperationCanceledException)
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
        catch (QuizException)
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
        catch (Exception ex)
        {
            await RollbackQuietlyAsync(transaction);
            _logger.LogError(ex, "Failed to save quiz {QuizId}.", quizId);
            throw new QuizException(500, "quiz_save_failed",
                "Could not save quiz progress. Please try again.");
        }
    }

    private async Task<Quiz> SaveNonRelationalAsync(Guid profileId, Guid quizId, SaveQuizRequest request, CancellationToken ct)
    {
        try
        {
            var quiz = await _db.Quizzes
                .SingleOrDefaultAsync(q => q.Id == quizId && q.UserId == profileId, ct)
                ?? throw new QuizException(404, "quiz_not_found", "Quiz not found.");

            ApplySaveMutation(quiz, request);
            await _db.SaveChangesAsync(ct);
            return quiz;
        }
        catch (QuizException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save quiz {QuizId}.", quizId);
            throw new QuizException(500, "quiz_save_failed",
                "Could not save quiz progress. Please try again.");
        }
    }

    private static async Task RollbackQuietlyAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // The transaction is disposed by the caller; rollback is best-effort after a failed command.
        }
    }

    private static void ApplySaveMutation(Quiz quiz, SaveQuizRequest request)
    {
        var questions = DeserializeQuestions(quiz.QuestionsJson);
        if (questions.Count == 0)
        {
            throw new QuizException(409, "quiz_has_no_questions", "Quiz has no questions to save.");
        }

        if (request.CurrentQuestionIndex < 0 || request.CurrentQuestionIndex >= questions.Count)
        {
            throw new QuizException(400, "invalid_question_index", "Current question index is invalid.");
        }

        var answers = DeserializeAnswers(quiz.AnswersJson);
        foreach (var (questionIndex, optionId) in request.Answers ?? new Dictionary<int, string?>())
        {
            var question = questions.FirstOrDefault(q => q.Index == questionIndex);
            if (question is null)
            {
                throw new QuizException(400, "invalid_question_index", "Answer question index is invalid.");
            }

            if (string.IsNullOrWhiteSpace(optionId) || !question.Options.Any(option => option.Id == optionId))
            {
                throw new QuizException(400, "invalid_option_id", "Answer option id is invalid.");
            }

            if (answers.TryGetValue(questionIndex, out var storedAnswer) && !string.Equals(storedAnswer, optionId, StringComparison.Ordinal))
            {
                throw new QuizException(409, "answer_already_submitted", "An answer has already been submitted for this question.");
            }

            answers[questionIndex] = optionId;
        }

        var submitted = answers
            .Where(answer => !string.IsNullOrWhiteSpace(answer.Value))
            .ToDictionary(answer => answer.Key, _ => true);
        var allQuestionsAnswered = questions.All(question => submitted.ContainsKey(question.Index));
        quiz.Status = allQuestionsAnswered ? QuizStatus.Completed : QuizStatus.InProgress;
        quiz.CurrentQuestionIndex = request.CurrentQuestionIndex;
        quiz.AnswersJson = JsonSerializer.Serialize(answers);
        quiz.SubmittedJson = JsonSerializer.Serialize(submitted);
        quiz.Score = allQuestionsAnswered
            ? questions.Count(question => answers.TryGetValue(question.Index, out var answer) && answer == question.CorrectOptionId)
            : null;
        quiz.UpdatedAt = DateTimeOffset.UtcNow;
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

    private static (QuizJsonParsed? Parsed, string? FailureReason) TryParseAndValidateQuizJson(
        string raw,
        int expectedCount,
        IReadOnlySet<string> previousQuestionKeys)
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

            var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var question in questions)
            {
                var normalizedQuestion = NormalizeQuestionForDuplicateComparison(question.Question);
                if (previousQuestionKeys.Contains(normalizedQuestion))
                {
                    return (null, "The generated quiz contains a duplicate question from this scope's recent quiz history.");
                }

                if (!duplicateKeys.Add(normalizedQuestion))
                {
                    return (null, "The generated quiz contains a duplicate question within the same batch.");
                }
            }

            return (new QuizJsonParsed(titleProp.GetString()!, questions.AsReadOnly()), null);
        }
    }

    private string? GetAlternateModel(string? activeModel)
    {
        var alternate = string.Equals(activeModel, _geminiOptions.Model, StringComparison.OrdinalIgnoreCase)
            ? _groqOptions.Model
            : _geminiOptions.Model;
        return string.IsNullOrWhiteSpace(alternate) || string.Equals(activeModel, alternate, StringComparison.OrdinalIgnoreCase)
            ? null
            : alternate;
    }

    private static string BuildCanonicalScopeJson(GenerateQuizRequest request)
    {
        var documentIds = request.DocumentIds?
            .Select(id => id.ToString("D"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var (scopeKind, scopeIds) = documentIds is { Length: > 0 }
            ? ("documents", documentIds)
            : request.DocumentId is { } documentId
                ? ("document", new[] { documentId.ToString("D") })
                : request.FolderId is { } folderId
                    ? ("folder", new[] { folderId.ToString("D") })
                    : ("none", Array.Empty<string>());
        return JsonSerializer.Serialize(new QuizScope(
            scopeKind,
            scopeIds,
            NormalizeScopeValue(request.SubjectCode),
            NormalizeScopeValue(request.Semester),
            NormalizeScopeValue(request.TopicKeyword)), JsonOptions);
    }

    private static string? NormalizeScopeValue(string? value)
    {
        var normalized = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 0 ? null : normalized.ToLowerInvariant();
    }

    private static string BuildExclusionNote(IReadOnlyList<string> questionTexts)
    {
        if (questionTexts.Count == 0)
        {
            return string.Empty;
        }

        const string header = "\n\n[EXCLUDED QUESTIONS — Do NOT generate these again]\n";
        var note = new StringBuilder(header);
        foreach (var question in questionTexts)
        {
            var line = $"- {question}\n";
            if (note.Length + line.Length > MaxExclusionPromptChars)
            {
                break;
            }

            note.Append(line);
        }

        return note.ToString();
    }

    private static string NormalizeQuestionForDuplicateComparison(string? question)
    {
        var normalized = string.Join(' ', (question ?? string.Empty).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return normalized.TrimEnd('.', '!', '?', ';', ':');
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
        var parsedQuestions = DeserializeQuestions(quiz.QuestionsJson);
        var answers = DeserializeAnswers(quiz.AnswersJson);
        var submitted = answers
            .Where(answer => !string.IsNullOrWhiteSpace(answer.Value))
            .ToDictionary(answer => answer.Key, _ => true);
        var questions = parsedQuestions
            .Select(question => submitted.ContainsKey(question.Index)
                ? new QuizQuestionDto(question.Index, question.Question, question.Subtitle, question.Options, question.CorrectOptionId, question.Explanation, question.SourceLabel)
                : new QuizQuestionDto(question.Index, question.Question, question.Subtitle, question.Options, null, null, question.SourceLabel))
            .ToList();

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

    private static List<QuizQuestionParsed> DeserializeQuestions(string questionsJson) =>
        string.IsNullOrWhiteSpace(questionsJson)
            ? new List<QuizQuestionParsed>()
            : JsonSerializer.Deserialize<List<QuizQuestionParsed>>(questionsJson, JsonOptions) ?? new List<QuizQuestionParsed>();

    private static Dictionary<int, string?> DeserializeAnswers(string? answersJson) =>
        string.IsNullOrWhiteSpace(answersJson)
            ? new Dictionary<int, string?>()
            : JsonSerializer.Deserialize<Dictionary<int, string?>>(answersJson, JsonOptions) ?? new Dictionary<int, string?>();

    private sealed record QuizJsonParsed(
        string Title,
        IReadOnlyList<QuizQuestionParsed> Questions);

    private sealed record QuizScope(
        string ScopeKind,
        IReadOnlyList<string> ScopeIds,
        string? SubjectCode,
        string? Semester,
        string? TopicKeyword);

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
