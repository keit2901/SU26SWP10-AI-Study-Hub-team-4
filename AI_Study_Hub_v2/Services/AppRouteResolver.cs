namespace AI_Study_Hub_v2.Services;

public static class AppRouteResolver
{
    public static string GetDashboardRoute(string? role)
    {
        if (IsAdminRole(role))
        {
            return "/admin/dashboard";
        }

        if (IsModeratorRole(role))
        {
            return "/moderator/dashboard";
        }

        return "/dashboard";
    }

    public static bool IsAdminRole(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

    public static bool IsModeratorRole(string? role) =>
        string.Equals(role, "Moderator", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Content Moderator", StringComparison.OrdinalIgnoreCase);
}
