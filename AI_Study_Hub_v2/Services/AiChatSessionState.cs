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

    public void Replace(IReadOnlyList<AiChatHistoryExchange> exchanges)
    {
        _history.Clear();
        _history.AddRange(exchanges);
    }

    public void UpdateExchangeQuiz(Guid quizId, QuizDto updated)
    {
        var idx = _history.FindIndex(e => e.Quiz?.Id == quizId);
        if (idx >= 0)
        {
            _history[idx] = _history[idx] with { Quiz = updated };
        }
    }

    public void RemoveExchangeByQuizId(Guid quizId)
    {
        _history.RemoveAll(e => e.Quiz?.Id == quizId);
    }
}

public sealed record AiChatHistoryExchange(
    string Question,
    string ScopeLabel,
    AiChatAnswerResponse Response,
    DateTimeOffset AskedAt,
    QuizDto? Quiz = null);
