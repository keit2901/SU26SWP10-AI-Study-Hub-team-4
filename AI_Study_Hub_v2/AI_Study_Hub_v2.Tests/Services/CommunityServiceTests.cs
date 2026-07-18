using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class CommunityServiceTests
{
    [Test]
    public async Task ReportFolderAsync_SharedForeignFolder_CreatesTrimmedPendingReport()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var reporter = SeedUser(db, 2, "Reporter");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);

        var id = await sut.ReportFolderAsync(
            reporter.SupabaseUserId,
            folder.Id,
            "  Contains content that should be reviewed.  ");

        db.CommunityReports.Should().ContainSingle(report =>
            report.Id == id
            && report.Status == "Pending"
            && report.Reason == "Contains content that should be reviewed."
            && report.ReportedByUserId == reporter.Id);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task ReportFolderAsync_BlankReason_Throws400(string reason)
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var sut = BuildSut(db);

        var act = () => sut.ReportFolderAsync(Guid.NewGuid(), Guid.NewGuid(), reason);

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.StatusCode.Should().Be(400);
        exception.Which.Code.Should().Be("invalid_reason");
    }

    [Test]
    public async Task ReportFolderAsync_ReasonTooLong_Throws400()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var sut = BuildSut(db);

        var act = () => sut.ReportFolderAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('x', 2_001));

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.Code.Should().Be("reason_too_long");
    }

    [Test]
    public async Task ReportFolderAsync_OwnSharedFolder_Throws400()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);

        var act = () => sut.ReportFolderAsync(owner.SupabaseUserId, folder.Id, "Review this");

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.Code.Should().Be("cannot_report_own_folder");
    }

    [Test]
    public async Task ReportFolderAsync_PrivateFolder_Throws400()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var reporter = SeedUser(db, 2, "Reporter");
        var folder = SeedFolder(db, owner.Id, isShared: false);
        var sut = BuildSut(db);

        var act = () => sut.ReportFolderAsync(reporter.SupabaseUserId, folder.Id, "Review this");

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.Code.Should().Be("folder_not_shared");
    }

    [Test]
    public async Task ReportFolderAsync_DuplicatePendingReport_Throws409()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var reporter = SeedUser(db, 2, "Reporter");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);
        await sut.ReportFolderAsync(reporter.SupabaseUserId, folder.Id, "First reason");

        var act = () => sut.ReportFolderAsync(reporter.SupabaseUserId, folder.Id, "Second reason");

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.StatusCode.Should().Be(409);
        exception.Which.Code.Should().Be("duplicate_report");
    }

    [TestCase(1, "Admin")]
    [TestCase(3, "Moderator")]
    public async Task GetPendingReportsAsync_ReviewerRole_ReturnsQueue(int roleId, string roleName)
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        EnsureModeratorRole(db);
        var owner = SeedUser(db, 2, "Owner");
        var reporter = SeedUser(db, 2, "Reporter");
        var reviewer = SeedUser(db, roleId, roleName);
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);
        await sut.ReportFolderAsync(reporter.SupabaseUserId, folder.Id, "Please review");

        var reports = await sut.GetPendingReportsAsync(reviewer.SupabaseUserId);

        reports.Should().ContainSingle(report =>
            report.FolderName == folder.Name
            && report.ReportedByName == "Reporter");
    }

    [Test]
    public async Task GetPendingReportsAsync_StudentReviewer_Throws403()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var student = SeedUser(db, 2, "Student");
        var sut = BuildSut(db);

        var act = () => sut.GetPendingReportsAsync(student.SupabaseUserId);

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.StatusCode.Should().Be(403);
        exception.Which.Code.Should().Be("reviewer_required");
    }

    [TestCase(1, "Admin")]
    [TestCase(3, "Moderator")]
    public async Task ResolveReportAsync_ReviewerRole_ResolvesCaseInsensitively(
        int roleId,
        string roleName)
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        EnsureModeratorRole(db);
        var owner = SeedUser(db, 2, "Owner");
        var reporter = SeedUser(db, 2, "Reporter");
        var reviewer = SeedUser(db, roleId, roleName);
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);
        var reportId = await sut.ReportFolderAsync(
            reporter.SupabaseUserId,
            folder.Id,
            "Please review");

        await sut.ResolveReportAsync(
            reviewer.SupabaseUserId,
            reportId,
            "resolved",
            "  Reviewed and handled.  ");

        var report = db.CommunityReports.Single(item => item.Id == reportId);
        report.Status.Should().Be("Resolved");
        report.Resolution.Should().Be("Reviewed and handled.");
        report.ResolvedByUserId.Should().Be(reviewer.Id);
        report.ResolvedAt.Should().NotBeNull();
    }

    [Test]
    public async Task ResolveReportAsync_StudentReviewer_Throws403()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var reporter = SeedUser(db, 2, "Reporter");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);
        var reportId = await sut.ReportFolderAsync(reporter.SupabaseUserId, folder.Id, "Please review");

        var act = () => sut.ResolveReportAsync(
            reporter.SupabaseUserId,
            reportId,
            "Resolved",
            "Student cannot resolve this.");

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.Code.Should().Be("reviewer_required");
    }

    [Test]
    public async Task ResolveReportAsync_BlankResolution_Throws400()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var reporter = SeedUser(db, 2, "Reporter");
        var admin = SeedUser(db, 1, "Admin");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);
        var reportId = await sut.ReportFolderAsync(reporter.SupabaseUserId, folder.Id, "Please review");

        var act = () => sut.ResolveReportAsync(admin.SupabaseUserId, reportId, "Resolved", "  ");

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.Code.Should().Be("resolution_required");
    }

    [Test]
    public async Task ResolveReportAsync_ResolutionTooLong_Throws400()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var reporter = SeedUser(db, 2, "Reporter");
        var admin = SeedUser(db, 1, "Admin");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);
        var reportId = await sut.ReportFolderAsync(reporter.SupabaseUserId, folder.Id, "Please review");

        var act = () => sut.ResolveReportAsync(
            admin.SupabaseUserId,
            reportId,
            "Resolved",
            new string('x', 2_001));

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.Code.Should().Be("resolution_too_long");
    }

    [Test]
    public async Task ResolveReportAsync_AlreadyHandledReport_Throws400()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var reporter = SeedUser(db, 2, "Reporter");
        var admin = SeedUser(db, 1, "Admin");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);
        var reportId = await sut.ReportFolderAsync(reporter.SupabaseUserId, folder.Id, "Please review");
        await sut.ResolveReportAsync(admin.SupabaseUserId, reportId, "Dismissed", "No violation found.");

        var act = () => sut.ResolveReportAsync(
            admin.SupabaseUserId,
            reportId,
            "Resolved",
            "Trying to process it twice.");

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.Code.Should().Be("report_already_resolved");
    }

    [TestCase("Pending")]
    [TestCase("Closed")]
    public async Task ResolveReportAsync_InvalidStatus_Throws400(string status)
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var reporter = SeedUser(db, 2, "Reporter");
        var admin = SeedUser(db, 1, "Admin");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);
        var reportId = await sut.ReportFolderAsync(reporter.SupabaseUserId, folder.Id, "Please review");

        var act = () => sut.ResolveReportAsync(
            admin.SupabaseUserId,
            reportId,
            status,
            "Reviewed.");

        var exception = await act.Should().ThrowAsync<CommunityException>();
        exception.Which.Code.Should().Be("invalid_status");
    }

    private static CommunityService BuildSut(Data.AppDbContext db) =>
        new(db, NullLogger<CommunityService>.Instance);

    private static void EnsureModeratorRole(Data.AppDbContext db)
    {
        if (db.Roles.Any(role => role.Id == 3))
        {
            return;
        }

        db.Roles.Add(new Role
        {
            Id = 3,
            RoleName = Role.ModeratorRoleName,
            Description = "Community moderator",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    private static User SeedUser(Data.AppDbContext db, int roleId, string fullName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..12],
            FullName = fullName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Folder SeedFolder(Data.AppDbContext db, Guid userId, bool isShared)
    {
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = $"Folder {Guid.NewGuid():N}"[..20],
            ShareStatus = isShared ? AI_Study_Hub_v2.Data.Entities.FolderStatus.Approved : AI_Study_Hub_v2.Data.Entities.FolderStatus.None,
            SharedAt = isShared ? DateTimeOffset.UtcNow : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(folder);
        db.SaveChanges();
        return folder;
    }
}
