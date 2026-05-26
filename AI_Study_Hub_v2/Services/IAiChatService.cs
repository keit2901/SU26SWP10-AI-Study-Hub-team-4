using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IAiChatService
{
    Task<AiChatAnswerResponse> AskAsync(
        Guid supabaseUserId,
        AiChatAskRequest request,
        CancellationToken cancellationToken = default);
}
