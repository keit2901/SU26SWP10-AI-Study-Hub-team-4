namespace AI_Study_Hub_v2.Options;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public DefaultAdminOptions? DefaultAdmin { get; set; }

    public DefaultModeratorOptions? DefaultModerator { get; set; }
}

public sealed class DefaultAdminOptions
{
    public string Email { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class DefaultModeratorOptions
{
    public string Email { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
