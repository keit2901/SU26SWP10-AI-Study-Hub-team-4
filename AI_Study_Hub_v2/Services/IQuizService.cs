using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IQuizService
{
    Task<QuizGenerateResponse> GenerateAsync(
        Guid supabaseUserId,
        QuizGenerateRequest request,
        CancellationToken cancellationToken = default);

    Task<QuizSubmitResponse> SubmitAsync(
        Guid supabaseUserId,
        Guid quizId,
        QuizSubmitRequest request,
        CancellationToken cancellationToken = default);
}
