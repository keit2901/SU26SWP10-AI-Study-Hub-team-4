namespace AI_Study_Hub_v2.Dtos;

public sealed record QuizGenerateRequest(
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
    IReadOnlyList<QuizQuestionDto> Questions,
    IReadOnlyList<QuizSourceDto> Sources,
    DateTimeOffset CreatedAt);

public sealed record QuizQuestionDto(
    string Id,
    string Text,
    IReadOnlyList<QuizOptionDto> Options,
    string? SourceLabel = null,
    string? Explanation = null);

public sealed record QuizOptionDto(
    string Id,
    string Text);

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
