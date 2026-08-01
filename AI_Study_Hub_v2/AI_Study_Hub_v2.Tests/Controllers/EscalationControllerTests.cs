using System.Reflection;
using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public sealed class EscalationControllerTests
{
    [Test]
    public async Task Create_CallsServiceWithLocalUserId_NotSupabaseUserId()
    {
        var escalationService = new Mock<IEscalationService>();
        var db = CreateInMemoryDbWithUser(out var localUserId, out var supabaseUserId);
        var controller = CreateController(escalationService.Object, db, supabaseUserId);
        var request = CreateRequest();
        escalationService.Setup(service => service.CreateAsync(localUserId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Dto(request.FolderId, "Pending"));

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        escalationService.Verify(service => service.CreateAsync(localUserId, request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Resolve_RetiredFolderWideRoute_ReturnsConflictWithoutCallingService()
    {
        var escalationService = new Mock<IEscalationService>();
        var db = CreateInMemoryDbWithUser(out _, out var supabaseUserId);
        var controller = CreateController(escalationService.Object, db, supabaseUserId);

        var result = await controller.Resolve(Guid.NewGuid(), new ResolveEscalationRequest { Status = "Approved" }, CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        var error = conflict.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.Code.Should().Be("escalation_batch_decision_retired");
        escalationService.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ResolveItems_UsesLocalUserIdAndReturnsUpdatedEscalation()
    {
        var escalationService = new Mock<IEscalationService>();
        var db = CreateInMemoryDbWithUser(out var localUserId, out var supabaseUserId);
        var controller = CreateController(escalationService.Object, db, supabaseUserId);
        var escalationId = Guid.NewGuid();
        var request = new ResolveEscalationItemsRequest
        {
            Items = [new ResolveEscalationItemRequest { ItemId = Guid.NewGuid(), Status = "Approved" }]
        };
        var resolved = Dto(Guid.NewGuid(), "Resolved");
        escalationService.Setup(service => service.ResolveItemsAsync(escalationId, request, localUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);

        var result = await controller.ResolveItems(escalationId, request, CancellationToken.None);

        var dto = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<DocumentEscalationDto>().Subject;
        dto.EscalationStatus.Should().Be("Resolved");
        escalationService.Verify(service => service.ResolveItemsAsync(escalationId, request, localUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void ResolveItems_IsAdminOnly()
    {
        var method = typeof(EscalationController).GetMethod(nameof(EscalationController.ResolveItems), BindingFlags.Instance | BindingFlags.Public)!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize!.Roles.Should().Be("Admin");
    }

    [Test]
    public async Task GetById_ReturnsAdminDetailContract()
    {
        var escalationService = new Mock<IEscalationService>();
        var db = CreateInMemoryDbWithUser(out _, out var supabaseUserId);
        var controller = CreateController(escalationService.Object, db, supabaseUserId);
        var id = Guid.NewGuid();
        escalationService.Setup(service => service.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Dto(Guid.NewGuid(), "Pending"));

        var result = await controller.GetById(id, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        escalationService.Verify(service => service.GetByIdAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void GetById_IsAdminOnly()
    {
        var method = typeof(EscalationController).GetMethod(nameof(EscalationController.GetById), BindingFlags.Instance | BindingFlags.Public)!;
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Admin");
    }

    private static CreateEscalationRequest CreateRequest() => new()
    {
        FolderId = Guid.NewGuid(),
        Reason = "Test escalation",
        Items = [new EscalationItemRequest { DocumentId = Guid.NewGuid(), RejectReason = "Docs fine." }]
    };

    private static DocumentEscalationDto Dto(Guid folderId, string status) => new(
        Guid.NewGuid(), folderId, "Moderator", "Reason", status, null, null, DateTimeOffset.UtcNow, null, [])
    {
        FolderName = "Folder"
    };

    private static EscalationController CreateController(IEscalationService escalation, AppDbContext db, Guid supabaseUserId)
    {
        var controller = new EscalationController(escalation, db, new Mock<ILogger<EscalationController>>().Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, supabaseUserId.ToString())]))
            }
        };
        return controller;
    }

    private static AppDbContext CreateInMemoryDbWithUser(out Guid localUserId, out Guid supabaseUserId)
    {
        var db = Support.TestDb.CreateInMemoryWithDocuments();
        localUserId = Guid.NewGuid();
        supabaseUserId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = localUserId, RoleId = 3, SupabaseUserId = supabaseUserId, Username = "mod1", FullName = "Moderator One",
            IsActive = true, DailyTokenQuota = 25_000, TokenUsageDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
        return db;
    }
}
