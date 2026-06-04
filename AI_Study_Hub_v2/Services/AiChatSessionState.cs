using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public sealed class AiChatSessionState
{
    private readonly List<AiChatHistoryExchange> _history = new();

    public IReadOnlyList<AiChatHistoryExchange> History => _history;

    public void Add(AiChatHistoryExchange exchange)
    {
        _history.Add(exchange);
    }

    public void Clear()
    {
        _history.Clear();
    }
}

public sealed record AiChatHistoryExchange(
    string Question,
    string ScopeLabel,
    AiChatAnswerResponse Response,
    DateTimeOffset AskedAt);
