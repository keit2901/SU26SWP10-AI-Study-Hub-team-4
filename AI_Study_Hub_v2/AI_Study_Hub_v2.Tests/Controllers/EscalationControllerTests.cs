using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public sealed class EscalationControllerTests
{
    // ── P1: Create MUST use local User.Id, not SupabaseUserId ──

    [Test]
    public async Task Create_CallsServiceWithLocalUserId_NotSupabaseUserId()
    {
        var escalationService = new Mock<IEscalationService>();
        var db = CreateInMemoryDbWithUser(out var localUserId, out var supabaseUserId);
        var controller = CreateController(escalationService.Object, db);

        // Simulate JWT claim with SupabaseUserId
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, supabaseUserId.ToString())
                }))
            }
        };

        var request = new CreateEscalationRequest
        {
            FolderId = Guid.NewGuid(),
            Reason = "Test escalation",
            Items = new List<EscalationItemRequest>
            {
                new() { DocumentId = Guid.NewGuid(), RejectReason = "Docs fine." }
            }
        };

        escalationService
            .Setup(s => s.CreateAsync(localUserId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEscalationDto(
                Guid.NewGuid(), request.FolderId, "Moderator", request.Reason,
                "Pending", null, null, DateTimeOffset.UtcNow, null,
                new List<DocumentEscalationItemDto>()));

        var result = await controller.Create(request, CancellationToken.None);

        var createdAt = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdAt.StatusCode.Should().Be(201);

        // Verify service was called with local User.Id, NOT SupabaseUserId
        escalationService.Verify(
            s => s.CreateAsync(
                It.Is<Guid>(id => id == localUserId && id != supabaseUserId),
                request,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── GetAll returns OK with list ──

    [Test]
    public async Task GetAll_ReturnsOkWithEscalations()
    {
        var escalationService = new Mock<IEscalationService>();
        var db = Support.TestDb.CreateInMemoryWithDocuments();
        var controller = CreateController(escalationService.Object, db);

        var escalations = new List<DocumentEscalationDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Mod", "R", "Pending", null, null, DateTimeOffset.UtcNow, null, new List<DocumentEscalationItemDto>()),
            new(Guid.NewGuid(), Guid.NewGuid(), "Mod", "R2", "Resolved", "OK", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new List<DocumentEscalationItemDto>())
        };

        escalationService.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(escalations);

        var result = await controller.GetAll(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(escalations);
    }

    // ── GetMy returns filtered list ──

    [Test]
    public async Task GetMy_ReturnsFilteredEscalationsForCurrentUser()
    {
        var escalationService = new Mock<IEscalationService>();
        var db = CreateInMemoryDbWithUser(out var localUserId, out var supabaseUserId);
        var controller = CreateController(escalationService.Object, db);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, supabaseUserId.ToString())
                }))
            }
        };

        var myEscalations = new List<DocumentEscalationDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Me", "My escalation", "Pending", null, null, DateTimeOffset.UtcNow, null, new List<DocumentEscalationItemDto>())
        };

        escalationService.Setup(s => s.GetMyAsync(localUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(myEscalations);

        var result = await controller.GetMy(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(myEscalations);
        escalationService.Verify(s => s.GetMyAsync(localUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Resolve success ──

    [Test]
    public async Task Resolve_ValidRequest_ReturnsUpdatedEscalation()
    {
        var escalationService = new Mock<IEscalationService>();
        var db = Support.TestDb.CreateInMemoryWithDocuments();
        var controller = CreateController(escalationService.Object, db);

        var escalationId = Guid.NewGuid();
        var resolveReq = new ResolveEscalationRequest { Status = "Approved", AdminResponse = "Valid escalation." };
        var resolved = new DocumentEscalationDto(
            escalationId, Guid.NewGuid(), "Mod", "R", "Approved", "Valid escalation.", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            new List<DocumentEscalationItemDto>());

        escalationService.Setup(s => s.ResolveAsync(escalationId, resolveReq, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);

        var result = await controller.Resolve(escalationId, resolveReq, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<DocumentEscalationDto>().Subject;
        dto.EscalationStatus.Should().Be("Approved");
        dto.AdminResponse.Should().Be("Valid escalation.");
    }

    // ── Helpers ──

    private static EscalationController CreateController(IEscalationService escalation, AppDbContext db)
    {
        var logger = new Mock<ILogger<EscalationController>>();
        return new EscalationController(escalation, db, logger.Object);
    }

    private static AppDbContext CreateInMemoryDbWithUser(out Guid localUserId, out Guid supabaseUserId)
    {
        var db = Support.TestDb.CreateInMemoryWithDocuments();
        localUserId = Guid.NewGuid();
        supabaseUserId = Guid.NewGuid();

        db.Users.Add(new User
        {
            Id = localUserId,
            RoleId = 3, // Moderator
            SupabaseUserId = supabaseUserId,
            Username = "mod1",
            FullName = "Moderator One",
            IsActive = true,
            DailyTokenQuota = 25_000,
            TokenUsageDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return db;
    }
}
