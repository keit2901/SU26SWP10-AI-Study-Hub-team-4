namespace AI_Study_Hub_v2.Dtos;

public sealed record AiAnswerReportRequest(
    string Question,
    string Answer,
    string Reason,
    string? Details = null,
    object? Context = null,
    IReadOnlyList<AiChatSourceDto>? Sources = null);

public sealed record AiAnswerReportResponse(
    Guid Id,
    string Status,
    DateTimeOffset CreatedAt);
