using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class QuizService : IQuizService
{
    private const int MinPromptLength = 3;
    private const int MaxQuestionCount = 10;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IRagSearchService _ragSearchService;

    public QuizService(AppDbContext db, IRagSearchService ragSearchService)
    {
        _db = db;
        _ragSearchService = ragSearchService;
    }

    public async Task<QuizGenerateResponse> GenerateAsync(
        Guid supabaseUserId,
        QuizGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await ResolveActiveUserAsync(supabaseUserId, cancellationToken);
        var prompt = NormalizePrompt(request.Prompt);
        var topK = request.TopK > 0 ? request.TopK : 5;

        IReadOnlyList<RagSearchResultDto> sources;
        try
        {
            sources = await _ragSearchService.SearchAsync(
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
            throw new AiStudyFeatureException(ex.StatusCode, ex.Code, ex.Message);
        }

        var usableSources = sources
            .Where(s => !string.IsNullOrWhiteSpace(s.ContentExcerpt))
            .Take(Math.Max(1, topK))
            .ToList();

        if (usableSources.Count == 0)
        {
            throw new AiStudyFeatureException(404, "no_quiz_sources", "No indexed document sources were found for quiz generation.");
        }

        var requestedCount = request.QuestionCount > 0 ? request.QuestionCount : 3;
        var questionCount = Math.Clamp(requestedCount, 1, Math.Min(MaxQuestionCount, usableSources.Count));
        var storedQuestions = BuildQuestions(prompt, usableSources, questionCount);
        var sourceDtos = usableSources.Select((source, index) => ToQuizSource(source, index)).ToList();
        var now = DateTimeOffset.UtcNow;
        var title = BuildTitle(prompt);

        var quiz = new Quiz
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = title,
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
            throw new AiStudyFeatureException(400, "quiz_id_required", "Quiz id is required.");
        }

        var quiz = await _db.Quizzes
            .FirstOrDefaultAsync(q => q.Id == quizId && q.UserId == user.Id, cancellationToken)
            ?? throw new AiStudyFeatureException(404, "quiz_not_found", "Quiz was not found for the authenticated user.");

        var questions = JsonSerializer.Deserialize<List<StoredQuizQuestion>>(quiz.QuestionsJson, JsonOptions) ?? new();
        if (questions.Count == 0)
        {
            throw new AiStudyFeatureException(409, "quiz_has_no_questions", "Quiz has no questions to grade.");
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
        var attempt = new QuizAttempt
        {
            Id = Guid.NewGuid(),
            QuizId = quiz.Id,
            UserId = user.Id,
            AnswersJson = JsonSerializer.Serialize(request.Answers ?? Array.Empty<QuizAnswerDto>(), JsonOptions),
            Score = score,
            Total = questions.Count,
            CreatedAt = now,
        };

        _db.QuizAttempts.Add(attempt);
        await _db.SaveChangesAsync(cancellationToken);

        return new QuizSubmitResponse(attempt.Id, quiz.Id, score, questions.Count, results, attempt.CreatedAt);
    }

    private async Task<User> ResolveActiveUserAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        if (supabaseUserId == Guid.Empty)
        {
            throw new AiStudyFeatureException(401, "missing_user_id", "Authenticated Supabase user id is missing or invalid.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new AiStudyFeatureException(404, "user_not_found", "Authenticated user has no profile in public.users.");

        if (!user.IsActive)
        {
            throw new AiStudyFeatureException(403, "user_inactive", "User account is inactive.");
        }

        return user;
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
            var correctId = "B";
            var options = new[]
            {
                new QuizOptionDto("A", $"It is unrelated to {Shorten(prompt, 48)}."),
                new QuizOptionDto(correctId, keyPhrase),
                new QuizOptionDto("C", "It is not mentioned in the retrieved source."),
                new QuizOptionDto("D", "It only applies when no documents are indexed."),
            };

            questions.Add(new StoredQuizQuestion(
                $"q{i + 1}",
                $"According to {label}, which statement best answers: {Shorten(prompt, 90)}?",
                options,
                correctId,
                $"The answer is grounded in {label}: {sentence}",
                label));
        }

        return questions;
    }

    private static QuizQuestionDto ToPublicQuestion(StoredQuizQuestion question) =>
        new(question.Id, question.Text, question.Options, question.SourceLabel, Explanation: null);

    private static QuizSourceDto ToQuizSource(RagSearchResultDto source, int index) =>
        new(
            string.IsNullOrWhiteSpace(source.SourceLabel) ? $"S{index + 1}" : source.SourceLabel,
            source.DocumentId,
            source.FileName,
            source.ChunkIndex,
            source.PageNumber,
            source.Score);

    private static string NormalizePrompt(string? prompt)
    {
        var normalized = string.Join(' ', (prompt ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length < MinPromptLength)
        {
            throw new AiStudyFeatureException(400, "prompt_required", "Quiz prompt or topic is required.");
        }

        return normalized;
    }

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string BuildTitle(string prompt) => $"Quiz: {Shorten(prompt, 80)}";

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

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private sealed record StoredQuizQuestion(
        string Id,
        string Text,
        IReadOnlyList<QuizOptionDto> Options,
        string CorrectOptionId,
        string Explanation,
        string? SourceLabel);
}
