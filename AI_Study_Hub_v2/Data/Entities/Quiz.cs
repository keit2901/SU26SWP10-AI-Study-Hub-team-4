namespace AI_Study_Hub_v2.Data.Entities;

public sealed class Quiz
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string QuestionsJson { get; set; } = "[]";

    public string ScopeJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;

    public ICollection<QuizAttempt> Attempts { get; set; } = new List<QuizAttempt>();
}
