using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Read-only cached plan catalog. Plans are loaded on first request and cached for 5 minutes.
/// </summary>
public interface IPlanService
{
    /// <summary>
    /// Returns the plan with the given key, or null if not found.
    /// </summary>
    Plan? GetPlanByKey(string key);

    /// <summary>
    /// Returns the free plan (PlanKey == "free").
    /// </summary>
    Plan GetFreePlan();

    /// <summary>
    /// Returns all currently active plans, ordered by SortOrder.
    /// </summary>
    IReadOnlyList<Plan> GetActivePlans();

    /// <summary>
    /// Clears the in-memory plan cache so the next read loads fresh data from the database.
    /// </summary>
    void InvalidateCache();
}
