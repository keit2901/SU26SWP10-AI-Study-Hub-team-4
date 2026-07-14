using System.Collections.Concurrent;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AI_Study_Hub_v2.Services;

public sealed class PlanService : IPlanService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly ConcurrentDictionary<string, Plan> PlanByKeyCache = new();
    private static Plan? _freePlan;
    private static IReadOnlyList<Plan>? _activePlans;

    private const string CacheKey = "rtk:plans:all";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public PlanService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public Plan? GetPlanByKey(string key)
    {
        EnsureLoaded();
        return PlanByKeyCache.TryGetValue(key, out var plan) ? plan : null;
    }

    public Plan GetFreePlan()
    {
        EnsureLoaded();
        return _freePlan ?? throw new InvalidOperationException("Free plan not found in the database.");
    }

    public IReadOnlyList<Plan> GetActivePlans()
    {
        EnsureLoaded();
        return _activePlans ?? Array.Empty<Plan>();
    }

    public void InvalidateCache()
    {
        lock (PlanByKeyCache)
        {
            _cache.Remove(CacheKey);
            PlanByKeyCache.Clear();
            _freePlan = null;
            _activePlans = null;
        }
    }

    private void EnsureLoaded()
    {
        // Use IMemoryCache as the synchronisation point for refreshing.
        if (_cache.TryGetValue(CacheKey, out _))
        {
            return;
        }

        lock (PlanByKeyCache)
        {
            // Double-check after acquiring the lock.
            if (_cache.TryGetValue(CacheKey, out _))
            {
                return;
            }

            var plans = _db.Plans
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                .ToList();

            PlanByKeyCache.Clear();
            _freePlan = null;

            foreach (var plan in plans)
            {
                PlanByKeyCache[plan.PlanKey] = plan;
                if (plan.PlanKey == "free")
                {
                    _freePlan = plan;
                }
            }

            _activePlans = plans;

            _cache.Set(CacheKey, true, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl,
                Size = 1,
            });
        }
    }
}
