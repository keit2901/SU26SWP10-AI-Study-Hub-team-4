using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Dtos;

// Sprint 2 quiz DTOs (chat-based)
public sealed record GenerateQuizRequest(
    Guid SessionId,
    IReadOnlyList<Guid>? DocumentIds = null,
    Guid? FolderId = null,
    Guid? DocumentId = null,
    string? Title = null,
    int Count = 8,
    string Difficulty = "medium",
    string? Model = null,
    string? ScopeLabel = null,
    string? SubjectCode = null,
    string? Semester = null,
    string? TopicKeyword = null);

public sealed record QuizDto(
    Guid Id,
    string Title,
    QuizStatus Status,
    int CurrentQuestionIndex,
    int TotalQuestions,
    IReadOnlyList<QuizQuestionDto> Questions,
    IReadOnlyDictionary<int, string?> Answers,
    IReadOnlyDictionary<int, bool> Submitted,
    int? Score,
    DateTimeOffset CreatedAt,
    string? ErrorMessage = null);

public sealed record QuizQuestionDto(
    int Index,
    string Question,
    string Subtitle,
    IReadOnlyList<QuizOptionDto> Options,
    string? CorrectOptionId,
    string? Explanation,
    string? SourceLabel);

// Sprint 3 quiz DTOs (standalone quiz APIs)
public sealed record QuizGenerateRequestV2(
    string Prompt,
    Guid? DocumentId = null,
    Guid? FolderId = null,
    IReadOnlyList<Guid>? DocumentIds = null,
    string? SubjectCode = null,
    string? Semester = null,
    int TopK = 5,
    int QuestionCount = 3);

public sealed record QuizGenerateResponse(
    Guid QuizId,
    string Title,
    IReadOnlyList<QuizQuestionDtoV2> Questions,
    IReadOnlyList<QuizSourceDto> Sources,
    DateTimeOffset CreatedAt);

public sealed record QuizQuestionDtoV2(
    string Id,
    string Text,
    IReadOnlyList<QuizOptionDto> Options,
    string? SourceLabel = null,
    string? Explanation = null);

public sealed record QuizOptionDto(
    string Id,
    string Text);

public sealed record SaveQuizRequest(
    int CurrentQuestionIndex,
    IReadOnlyDictionary<int, string?> Answers);

public sealed record QuizSourceDto(
    string Label,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? PageNumber,
    double? Score);

public sealed record QuizSubmitRequest(
    IReadOnlyList<QuizAnswerDto> Answers);

public sealed record QuizAnswerDto(
    string QuestionId,
    string OptionId);

public sealed record QuizSubmitResponse(
    Guid AttemptId,
    Guid QuizId,
    int Score,
    int Total,
    IReadOnlyList<QuizQuestionResultDto> Results,
    DateTimeOffset CreatedAt);

public sealed record QuizQuestionResultDto(
    string QuestionId,
    bool IsCorrect,
    string? SubmittedOptionId,
    string CorrectOptionId,
    string Explanation);
