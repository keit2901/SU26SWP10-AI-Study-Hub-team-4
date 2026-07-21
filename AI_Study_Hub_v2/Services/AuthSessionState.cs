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

    public string CurrentPlan { get; private set; } = "free";

    public StorageQuotaSnapshotDto? CurrentPlanSnapshot { get; private set; }

    public string CurrentPlanDisplayName => CurrentPlanSnapshot?.PlanDisplayName ?? FormatPlanDisplayName(CurrentPlan);

    public event Action? OnChange;

    private static readonly HashSet<string> ValidPlanKeys =
        new(StringComparer.OrdinalIgnoreCase) { "free", "pro", "unlimited" };

    public void SetPlan(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan) || !ValidPlanKeys.Contains(plan))
        {
            return;
        }

        CurrentPlan = NormalizePlanKey(plan);
        CurrentPlanSnapshot = null;
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
            CurrentPlan = NormalizePlanKey(snapshot.PlanKey);
            CurrentPlanSnapshot = snapshot;
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
        CurrentPlan = "free";
        CurrentPlanSnapshot = null;
        NotifyChanged();
    }

    public void Clear()
    {
        Session = null;
        CurrentPlan = "free";
        CurrentPlanSnapshot = null;
        NotifyChanged();
    }

    private static string NormalizePlanKey(string plan) => plan.Trim().ToLowerInvariant();

    private static string FormatPlanDisplayName(string plan) => plan switch
    {
        null or "" => "Free",
        "pro" => "Pro",
        "unlimited" => "Unlimited",
        _ => $"{char.ToUpper(plan[0])}{plan[1..].ToLowerInvariant()}",
    };

    private void NotifyChanged() => OnChange?.Invoke();
}
