using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IQuizService
{
    // Sprint 2: Chat-based quiz
    Task<QuizDto> GenerateAsync(Guid supabaseUserId, GenerateQuizRequest request, CancellationToken ct = default);

    Task<QuizDto> ResumeAsync(Guid supabaseUserId, Guid sessionId, CancellationToken ct = default);

    Task SaveAsync(Guid supabaseUserId, Guid quizId, SaveQuizRequest request, CancellationToken ct = default);

    Task<QuizDto?> GetByIdAsync(Guid supabaseUserId, Guid quizId, CancellationToken ct = default);

    // Sprint 3: Standalone quiz APIs
    Task<QuizGenerateResponse> GenerateAsyncV2(
        Guid supabaseUserId,
        QuizGenerateRequestV2 request,
        CancellationToken cancellationToken = default);

    Task<QuizSubmitResponse> SubmitAsync(
        Guid supabaseUserId,
        Guid quizId,
        QuizSubmitRequest request,
        CancellationToken cancellationToken = default);
}
