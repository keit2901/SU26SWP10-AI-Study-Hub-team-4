namespace AI_Study_Hub_v2.Data.Entities;

public enum QuizStatus
{
    GeneratingFailed,
    InProgress,
    Completed,
}

public sealed class Quiz
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Title { get; set; } = "Quiz";

    public QuizStatus Status { get; set; } = QuizStatus.InProgress;

    public string? ErrorCode { get; set; }

    public int CurrentQuestionIndex { get; set; }

    public int TotalQuestions { get; set; } = 8;

    public string QuestionsJson { get; set; } = "[]";

    public string AnswersJson { get; set; } = "{}";

    public string SubmittedJson { get; set; } = "{}";

    public int? Score { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ChatSession Session { get; set; } = null!;
}
