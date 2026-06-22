using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IQuizService
{
    Task<QuizDto> GenerateAsync(Guid supabaseUserId, GenerateQuizRequest request, CancellationToken ct = default);

    Task<QuizDto> ResumeAsync(Guid supabaseUserId, Guid sessionId, CancellationToken ct = default);

    Task SaveAsync(Guid supabaseUserId, Guid quizId, SaveQuizRequest request, CancellationToken ct = default);

    Task<QuizDto?> GetByIdAsync(Guid supabaseUserId, Guid quizId, CancellationToken ct = default);
}
