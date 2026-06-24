using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Dtos;

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
    string? Semester = null);

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
    string CorrectOptionId,
    string Explanation,
    string? SourceLabel);

public sealed record QuizOptionDto(
    string Id,
    string Text);

public sealed record SaveQuizRequest(
    QuizStatus Status,
    int CurrentQuestionIndex,
    IReadOnlyDictionary<int, string?> Answers,
    IReadOnlyDictionary<int, bool> Submitted,
    int? Score);
