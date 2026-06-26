namespace AI_Study_Hub_v2.Data.Entities;

public sealed class QuizAttempt
{
    public Guid Id { get; set; }

    public Guid QuizId { get; set; }

    public Guid UserId { get; set; }

    public string AnswersJson { get; set; } = "[]";

    public int Score { get; set; }

    public int Total { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Quiz Quiz { get; set; } = null!;

    public User User { get; set; } = null!;
}
