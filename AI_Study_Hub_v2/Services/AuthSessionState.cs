using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Per-circuit (scoped) holder for the demo session. Phase 1 keeps tokens
/// in memory only — no cookie / localStorage. Refresh page = logged out.
/// Good enough for the demo and avoids JS interop concerns during prerender.
/// </summary>
public sealed class AuthSessionState
{
    public AuthResponse? Session { get; private set; }

    public bool IsAuthenticated => Session is not null
        && !string.IsNullOrWhiteSpace(Session.AccessToken)
        && Session.ExpiresAt > DateTimeOffset.UtcNow;

    public UserDto? CurrentUser => Session?.User;

    public string? AccessToken => Session?.AccessToken;

    public string? CurrentPassword { get; set; }

    /// <summary>
    /// In-memory plan tier for demo purposes. Defaults to "Free".
    /// Valid values: "Free", "Pro", "Unlimited".
    /// </summary>
    public string CurrentPlan { get; private set; } = "Free";

    public event Action? OnChange;

    public void SetPlan(string plan)
    {
        CurrentPlan = plan;
        NotifyChanged();
    }

    public void Set(AuthResponse session)
    {
        Session = session;
        NotifyChanged();
    }

    public void Clear()
    {
        Session = null;
        CurrentPassword = null;
        CurrentPlan = "Free";
        NotifyChanged();
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
