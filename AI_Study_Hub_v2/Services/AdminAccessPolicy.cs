namespace AI_Study_Hub_v2.Services;

public static class AdminAccessPolicy
{
    public static bool IsAdmin(string? role)
        => role?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true;

    public static string GetAuthenticatedLandingPage(string? role)
        => IsAdmin(role) ? "/admin" : "/profile";
}
