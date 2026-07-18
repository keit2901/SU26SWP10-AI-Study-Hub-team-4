namespace AI_Study_Hub_v2.Services;

public static class AdminAccessPolicy
{
    public static bool IsAdmin(string? role)
        => role?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true;

    public static string GetAuthenticatedLandingPage(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return "/documents";
        return role.Trim().ToLowerInvariant() switch
        {
            "admin"     => "/admin",
            "moderator" => "/dashboard",
            _           => "/documents"
        };
    }

    public static bool IsDashboardPath(string? relativePath)
        => relativePath?.StartsWith("dashboard", StringComparison.OrdinalIgnoreCase) == true;
}
