using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class StorageQuotaServiceTests
{
    private Mock<IPlanService> _planServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _planServiceMock = new Mock<IPlanService>();
    }

    private User SeedUser(AppDbContext db, Guid supabaseUserId, long storageUsedBytes = 0)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = supabaseUserId,
            Username = "testuser",
            FullName = "Test User",
            StorageUsedBytes = storageUsedBytes,
            RoleId = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private Plan SeedPlan(AppDbContext db, string key, int? maxDocs = null, int? maxFolders = null, long? storageQuota = null)
    {
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            PlanKey = key,
            DisplayName = key + " Plan",
            MaxDocumentCount = maxDocs,
            MaxFolderCount = maxFolders,
            StorageQuotaBytes = storageQuota,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Plans.Add(plan);
        db.SaveChanges();
        return plan;
    }

    private void SeedUserPlan(AppDbContext db, Guid userId, Guid planId, string status = "active", DateTimeOffset? expiresAt = null)
    {
        var userPlan = new UserPlan
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanId = planId,
            Status = status,
            AssignedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAt = expiresAt
        };
        db.UserPlans.Add(userPlan);
        db.SaveChanges();
    }

    [Test]
    public async Task ReserveUploadAsync_InvalidSizeBytes_ThrowsArgumentOutOfRangeException()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var sut = new StorageQuotaService(db, _planServiceMock.Object);

        var act = () => sut.ReserveUploadAsync(Guid.NewGuid(), 0, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ReserveUploadAsync_UserNotFound_ThrowsPlanException()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var sut = new StorageQuotaService(db, _planServiceMock.Object);

        var act = () => sut.ReserveUploadAsync(Guid.NewGuid(), 1024, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<PlanException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("user_not_found");
    }

    [Test]
    [Ignore("InMemory EF provider does not support ExecuteSqlRawAsync. Atomicity is verified by RecordDelete tests (same pattern) and SERIALIZABLE transaction tests.")]
    public async Task ReleaseReservationAsync_DecreasesUserStorageUsedBytes()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, Guid.NewGuid(), storageUsedBytes: 5000);
        var sut = new StorageQuotaService(db, _planServiceMock.Object);

        var reservation = new StorageReservation(user.Id, 2000, DateTimeOffset.UtcNow);
        await sut.ReleaseReservationAsync(reservation, CancellationToken.None);

        var updatedUser = await db.Users.FindAsync(user.Id);
        updatedUser!.StorageUsedBytes.Should().Be(3000);
    }

    [Test]
    public async Task GetSnapshotAsync_UserPlanActive_ReturnsCorrectSnapshot()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var supabaseUserId = Guid.NewGuid();
        var user = SeedUser(db, supabaseUserId, storageUsedBytes: 1500);
        var plan = SeedPlan(db, "pro", storageQuota: 10000);
        SeedUserPlan(db, user.Id, plan.Id);

        var sut = new StorageQuotaService(db, _planServiceMock.Object);

        var snapshot = await sut.GetSnapshotAsync(supabaseUserId, CancellationToken.None);

        snapshot.UsedBytes.Should().Be(1500);
        snapshot.QuotaBytes.Should().Be(10000);
        snapshot.PlanKey.Should().Be("pro");
        snapshot.PlanDisplayName.Should().Be("pro Plan");
    }

    [Test]
    public async Task GetSnapshotAsync_NoActivePlan_FallsBackToFreePlan()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var supabaseUserId = Guid.NewGuid();
        var user = SeedUser(db, supabaseUserId, storageUsedBytes: 500);
        
        var freePlan = new Plan
        {
            Id = Guid.NewGuid(),
            PlanKey = "free",
            DisplayName = "Free Plan",
            StorageQuotaBytes = 5000,
            IsActive = true
        };
        _planServiceMock.Setup(p => p.GetFreePlan()).Returns(freePlan);

        var sut = new StorageQuotaService(db, _planServiceMock.Object);

        var snapshot = await sut.GetSnapshotAsync(supabaseUserId, CancellationToken.None);

        snapshot.UsedBytes.Should().Be(500);
        snapshot.QuotaBytes.Should().Be(5000);
        snapshot.PlanKey.Should().Be("free");
        snapshot.PlanDisplayName.Should().Be("Free Plan");
    }

    [Test]
    public async Task ValidateDocumentCountAsync_ExceedsLimit_ThrowsPlanException()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var supabaseUserId = Guid.NewGuid();
        var user = SeedUser(db, supabaseUserId);
        var plan = SeedPlan(db, "free", maxDocs: 1);
        SeedUserPlan(db, user.Id, plan.Id);

        // Seed 1 document for user
        db.Documents.Add(new Document
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FileName = "doc1.pdf",
            StoragePath = "path1",
            FileSizeBytes = 100,
            MimeType = "application/pdf",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();

        var sut = new StorageQuotaService(db, _planServiceMock.Object);

        var act = () => sut.ValidateDocumentCountAsync(supabaseUserId, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<PlanException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        ex.Which.Code.Should().Be("document_count_exceeded");
    }

    [Test]
    public async Task ValidateDocumentCountAsync_WithinLimit_DoesNotThrow()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var supabaseUserId = Guid.NewGuid();
        var user = SeedUser(db, supabaseUserId);
        var plan = SeedPlan(db, "free", maxDocs: 5);
        SeedUserPlan(db, user.Id, plan.Id);

        var sut = new StorageQuotaService(db, _planServiceMock.Object);

        var act = () => sut.ValidateDocumentCountAsync(supabaseUserId, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ValidateFolderCountAsync_ExceedsLimit_ThrowsPlanException()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var supabaseUserId = Guid.NewGuid();
        var user = SeedUser(db, supabaseUserId);
        var plan = SeedPlan(db, "free", maxFolders: 1);
        SeedUserPlan(db, user.Id, plan.Id);

        // Seed 1 folder for user
        db.Folders.Add(new Folder
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "Folder 1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();

        var sut = new StorageQuotaService(db, _planServiceMock.Object);

        var act = () => sut.ValidateFolderCountAsync(supabaseUserId, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<PlanException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        ex.Which.Code.Should().Be("folder_count_exceeded");
    }

    [Test]
    public async Task ValidateFolderCountAsync_WithinLimit_DoesNotThrow()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var supabaseUserId = Guid.NewGuid();
        var user = SeedUser(db, supabaseUserId);
        var plan = SeedPlan(db, "free", maxFolders: 5);
        SeedUserPlan(db, user.Id, plan.Id);

        var sut = new StorageQuotaService(db, _planServiceMock.Object);

        var act = () => sut.ValidateFolderCountAsync(supabaseUserId, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
