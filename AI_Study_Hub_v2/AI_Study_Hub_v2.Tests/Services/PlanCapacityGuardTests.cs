using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.AspNetCore.Http;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class PlanCapacityGuardTests
{
    private static User SeedUser(AppDbContext db, bool isActive = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..15],
            FullName = "Capacity Test User",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Plan SeedActivePlan(AppDbContext db, User user, int? maxDocuments = null,
        int? maxFolders = null, int? maxDocumentsPerFolder = null)
    {
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            PlanKey = $"plan-{Guid.NewGuid():N}",
            DisplayName = "Capacity plan",
            MaxDocumentCount = maxDocuments,
            MaxFolderCount = maxFolders,
            MaxDocsPerFolder = maxDocumentsPerFolder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Plans.Add(plan);
        db.UserPlans.Add(new UserPlan
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PlanId = plan.Id,
            Status = "active",
            AssignedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return plan;
    }

    private static Folder SeedFolder(AppDbContext db, Guid userId)
    {
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = $"Folder {Guid.NewGuid():N}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(folder);
        db.SaveChanges();
        return folder;
    }

    private static void SeedDocument(AppDbContext db, Guid userId, Guid? folderId = null)
    {
        db.Documents.Add(new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderId,
            FileName = $"{Guid.NewGuid():N}.pdf",
            StoragePath = $"users/{userId:N}/{Guid.NewGuid():N}",
            FileSizeBytes = 1,
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = DocumentStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    [Test]
    public async Task LockAndValidateAsync_ActivePlanDocumentLimit_ThrowsExactPlanError()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db);
        SeedActivePlan(db, user, maxDocuments: 1);
        SeedDocument(db, user.Id);
        var sut = new PlanCapacityGuard(Mock.Of<IPlanService>());

        var act = () => sut.LockAndValidateAsync(db, user.Id, new PlanCapacityRequest(1, 0, null, 0), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<PlanException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        exception.Which.Code.Should().Be("document_count_exceeded");
    }

    [Test]
    public async Task LockAndValidateAsync_ActivePlanFolderLimit_ThrowsExactPlanError()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db);
        SeedActivePlan(db, user, maxFolders: 1);
        SeedFolder(db, user.Id);
        var sut = new PlanCapacityGuard(Mock.Of<IPlanService>());

        var act = () => sut.LockAndValidateAsync(db, user.Id, new PlanCapacityRequest(0, 1, null, 0), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<PlanException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        exception.Which.Code.Should().Be("folder_count_exceeded");
    }

    [Test]
    public async Task LockAndValidateAsync_ActivePlanFolderDocumentLimit_ThrowsExactDocumentError()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db);
        SeedActivePlan(db, user, maxDocumentsPerFolder: 1);
        var folder = SeedFolder(db, user.Id);
        SeedDocument(db, user.Id, folder.Id);
        var sut = new PlanCapacityGuard(Mock.Of<IPlanService>());

        var act = () => sut.LockAndValidateAsync(db, user.Id, new PlanCapacityRequest(1, 0, folder.Id, 1), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DocumentException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        exception.Which.Code.Should().Be("folder_full");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task LockAndValidateAsync_MissingOrInactiveUser_ThrowsUserNotFound(bool inactiveUser)
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var userId = inactiveUser ? SeedUser(db, isActive: false).Id : Guid.NewGuid();
        var sut = new PlanCapacityGuard(Mock.Of<IPlanService>());

        var act = () => sut.LockAndValidateAsync(db, userId, new PlanCapacityRequest(0, 0, null, 0), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<PlanException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        exception.Which.Code.Should().Be("user_not_found");
    }
}
