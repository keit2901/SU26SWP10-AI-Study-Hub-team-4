namespace AI_Study_Hub_v2.Data.Entities;

public sealed class Role
{
    public int Id { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();

    public const string AdminRoleName = "Admin";
    public const string StudentRoleName = "Student";
}
