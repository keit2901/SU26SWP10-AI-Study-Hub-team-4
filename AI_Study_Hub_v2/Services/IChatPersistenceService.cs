using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IChatPersistenceService
{
    Task<IReadOnlyList<ChatSessionDto>> ListSessionsAsync(Guid supabaseUserId, Guid? folderId = null, CancellationToken ct = default);

    Task<ChatSessionDto> CreateSessionAsync(Guid supabaseUserId, CreateChatSessionRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(Guid supabaseUserId, Guid sessionId, CancellationToken ct = default);

    Task DeleteSessionAsync(Guid supabaseUserId, Guid sessionId, CancellationToken ct = default);

    Task SaveExchangeAsync(Guid supabaseUserId, Guid sessionId, string question, string scopeLabel, AiChatAnswerResponse response, CancellationToken ct = default);

    Task SaveQuizExchangeAsync(Guid supabaseUserId, Guid sessionId, string scopeLabel, string userContent, Guid quizId, string quizTitle, string quizStatus, int? totalQuestions = null, int? score = null, CancellationToken ct = default);

    Task UpdateQuizMetadataAsync(Guid supabaseUserId, Guid quizId, string quizStatus, int? totalQuestions = null, int? score = null, CancellationToken ct = default);
}
