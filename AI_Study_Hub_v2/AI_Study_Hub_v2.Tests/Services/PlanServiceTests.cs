using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class PlanServiceTests
{
    private IMemoryCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());

        // Clear static cache fields to ensure test isolation
        var planByKeyCacheField = typeof(PlanService).GetField("PlanByKeyCache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (planByKeyCacheField?.GetValue(null) is System.Collections.Concurrent.ConcurrentDictionary<string, Plan> cache)
        {
            cache.Clear();
        }

        var freePlanField = typeof(PlanService).GetField("_freePlan", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        freePlanField?.SetValue(null, null);

        var activePlansField = typeof(PlanService).GetField("_activePlans", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        activePlansField?.SetValue(null, null);
    }

    [TearDown]
    public void TearDown()
    {
        _cache.Dispose();
    }

    private static Plan CreatePlan(string key, string displayName, int sortOrder, bool isActive = true)
    {
        return new Plan
        {
            Id = Guid.NewGuid(),
            PlanKey = key,
            DisplayName = displayName,
            SortOrder = sortOrder,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [Test]
    public void GetActivePlans_ReturnsOnlyActivePlans_OrderedBySortOrder()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var p1 = CreatePlan("premium", "Premium", 2, isActive: true);
        var p2 = CreatePlan("free", "Free", 1, isActive: true);
        var p3 = CreatePlan("inactive", "Inactive", 3, isActive: false);
        db.Plans.AddRange(p1, p2, p3);
        db.SaveChanges();

        var sut = new PlanService(db, _cache);

        var active = sut.GetActivePlans();

        active.Should().HaveCount(2);
        active[0].PlanKey.Should().Be("free");
        active[1].PlanKey.Should().Be("premium");
    }

    [Test]
    public void GetPlanByKey_ReturnsCorrectPlan_WhenExists()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var freePlan = CreatePlan("free", "Free Plan", 1);
        db.Plans.Add(freePlan);
        db.SaveChanges();

        var sut = new PlanService(db, _cache);

        var result = sut.GetPlanByKey("free");

        result.Should().NotBeNull();
        result!.PlanKey.Should().Be("free");
    }

    [Test]
    public void GetPlanByKey_ReturnsNull_WhenDoesNotExist()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var freePlan = CreatePlan("free", "Free Plan", 1);
        db.Plans.Add(freePlan);
        db.SaveChanges();

        var sut = new PlanService(db, _cache);

        var result = sut.GetPlanByKey("pro");

        result.Should().BeNull();
    }

    [Test]
    public void GetFreePlan_ReturnsFreePlan_WhenExists()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var freePlan = CreatePlan("free", "Free Plan", 1);
        db.Plans.Add(freePlan);
        db.SaveChanges();

        var sut = new PlanService(db, _cache);

        var result = sut.GetFreePlan();

        result.Should().NotBeNull();
        result.PlanKey.Should().Be("free");
    }

    [Test]
    public void GetFreePlan_ThrowsInvalidOperationException_WhenFreePlanMissing()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var proPlan = CreatePlan("pro", "Pro Plan", 1);
        db.Plans.Add(proPlan);
        db.SaveChanges();

        var sut = new PlanService(db, _cache);

        var act = () => sut.GetFreePlan();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Free plan not found*");
    }
}
