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

    /// <summary>
    /// In-memory plan tier for demo purposes. Defaults to "Free".
    /// Valid values: "Free", "Pro", "Unlimited".
    /// </summary>
    public string CurrentPlan { get; private set; } = "Free";

    public event Action? OnChange;

    private static readonly HashSet<string> ValidPlanKeys =
        new(StringComparer.OrdinalIgnoreCase) { "free", "pro", "unlimited" };

    public void SetPlan(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan) || !ValidPlanKeys.Contains(plan))
            return;

        CurrentPlan = plan;
        NotifyChanged();
    }

    /// <summary>
    /// Loads the user's current plan from the API and updates <see cref="CurrentPlan"/>.
    /// No-op if the user is not authenticated.
    /// </summary>
    public async Task LoadCurrentPlanAsync(PlanApiClient planApiClient, CancellationToken ct = default)
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(AccessToken))
        {
            CurrentPlan = "Free";
            return;
        }

        try
        {
            var snapshot = await planApiClient.GetCurrentPlanAsync(AccessToken, ct);
            CurrentPlan = snapshot.PlanKey;
        }
        catch
        {
            // Keep whatever the current plan is if the API call fails
        }

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
        CurrentPlan = "Free";
        NotifyChanged();
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
