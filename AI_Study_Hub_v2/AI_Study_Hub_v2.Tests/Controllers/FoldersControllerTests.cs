using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public class FoldersControllerTests
{
    private static FoldersController BuildSut(IFolderService service, ClaimsPrincipal? user = null)
    {
        var ctrl = new FoldersController(service, Mock.Of<IShareReviewService>(), NullLogger<FoldersController>.Instance);
        var http = new DefaultHttpContext();
        if (user is not null)
        {
            http.User = user;
        }
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    private static ClaimsPrincipal Principal(Guid? supabaseUserId = null, bool useSubInsteadOfNameId = false)
    {
        var claims = new List<Claim>();
        if (supabaseUserId.HasValue)
        {
            claims.Add(new Claim(
                useSubInsteadOfNameId ? "sub" : ClaimTypes.NameIdentifier,
                supabaseUserId.Value.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    private static FolderDto SampleFolder(Guid? id = null, string name = "Sprint demo") => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        DocumentCount = 2,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Test]
    public async Task List_HappyPath_Returns200_AndForwardsSubClaim()
    {
        var supabaseUserId = Guid.NewGuid();
        var rows = new List<FolderDto> { SampleFolder() };
        Guid? captured = null;
        var svc = new Mock<IFolderService>();
        svc.Setup(s => s.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((uid, _) => captured = uid)
            .ReturnsAsync(rows);

        var sut = BuildSut(svc.Object, Principal(supabaseUserId, useSubInsteadOfNameId: true));

        var result = await sut.List(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(rows);
        captured.Should().Be(supabaseUserId);
    }

    [Test]
    public async Task ListShared_AnonymousRequest_ForwardsNullViewer()
    {
        var rows = new List<FolderDto> { SampleFolder() };
        Guid? captured = Guid.NewGuid();
        var svc = new Mock<IFolderService>();
        svc.Setup(service => service.ListSharedAsync(
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid?, CancellationToken>((viewerId, _) => captured = viewerId)
            .ReturnsAsync(rows);
        var sut = BuildSut(svc.Object);

        var result = await sut.ListShared(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(rows);
        captured.Should().BeNull();
    }

    [Test]
    public async Task ListShared_AuthenticatedRequest_ForwardsViewerClaim()
    {
        var viewerId = Guid.NewGuid();
        Guid? captured = null;
        var svc = new Mock<IFolderService>();
        svc.Setup(service => service.ListSharedAsync(
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid?, CancellationToken>((id, _) => captured = id)
            .ReturnsAsync(new List<FolderDto>());
        var sut = BuildSut(svc.Object, Principal(viewerId));

        var result = await sut.ListShared(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        captured.Should().Be(viewerId);
    }

    [Test]
    public async Task Create_HappyPath_Returns201_CreatedAtAction()
    {
        var dto = SampleFolder();
        var svc = new Mock<IFolderService>();
        svc.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateFolderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Create(new CreateFolderRequest { Name = "Sprint demo" }, CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.ActionName.Should().Be(nameof(FoldersController.List));
        created.Value.Should().BeSameAs(dto);
    }

    [Test]
    public async Task Create_WhenServiceThrowsDocumentException_MapsStatusAndCode()
    {
        var svc = new Mock<IFolderService>();
        svc.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateFolderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentException(409, "folder_name_taken", "duplicate"));

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Create(new CreateFolderRequest { Name = "Sprint demo" }, CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(409);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("folder_name_taken");
    }

    [Test]
    public async Task Create_WhenServiceThrowsPlanException_MapsStatusAndCode()
    {
        var svc = new Mock<IFolderService>();
        svc.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateFolderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PlanException(402, "folder_count_exceeded", "Folder limit reached."));

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Create(new CreateFolderRequest { Name = "Sprint demo" }, CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(402);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("folder_count_exceeded");
    }

    [Test]
    public async Task Update_HappyPath_Returns200_WithDto()
    {
        var dto = SampleFolder(name: "Renamed");
        var svc = new Mock<IFolderService>();
        svc.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), dto.Id, It.IsAny<UpdateFolderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Update(dto.Id, new UpdateFolderRequest { Name = "Renamed" }, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(dto);
    }

    [Test]
    public async Task Delete_HappyPath_Returns204_NoContent()
    {
        var svc = new Mock<IFolderService>();
        svc.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Delete(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Test]
    public async Task List_InvalidSubClaim_Returns401_MissingUserId()
    {
        var svc = new Mock<IFolderService>(MockBehavior.Strict);
        var sut = BuildSut(svc.Object, Principal());

        var result = await sut.List(CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(401);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("missing_user_id");
    }

    [Test]
    public async Task ShareReviewActions_ReturnRetiredBatchModerationConflict_AndAutoCheckForwardsRequest()
    {
        var callerId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        var dto = SampleFolder(folderId);
        var svc = new Mock<IFolderService>();
        svc.Setup(service => service.AutoCheckFolderShareAsync(callerId, folderId, cancellationToken))
            .ReturnsAsync(dto);
        var sut = BuildSut(svc.Object, Principal(callerId, useSubInsteadOfNameId: true));

        var approve = sut.ApproveShare(folderId, cancellationToken).Result.Should().BeOfType<ConflictObjectResult>().Subject;
        approve.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        approve.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("folder_batch_moderation_retired");

        var reject = sut.RejectShare(folderId, new RejectFolderShareRequest { Reason = "reason" }, cancellationToken)
            .Result.Should().BeOfType<ConflictObjectResult>().Subject;
        reject.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        reject.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("folder_batch_moderation_retired");

        (await sut.AutoCheckShare(folderId, cancellationToken)).Result.Should().BeOfType<OkObjectResult>();

        svc.VerifyAll();
    }

    [Test]
    public void ShareReviewActions_KeepAdminModeratorRoleAuthorization()
    {
        foreach (var methodName in new[]
        {
            nameof(FoldersController.ApproveShare),
            nameof(FoldersController.RejectShare),
            nameof(FoldersController.AutoCheckShare),
        })
        {
            var authorize = typeof(FoldersController)
                .GetMethod(methodName)!
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
                .Single();

            authorize.Roles.Should().Be("Admin,Moderator");
        }
    }

    [Test]
    public async Task GetReviewerShareReview_ForwardsAuthenticatedReviewerToDedicatedServiceMethod()
    {
        var callerId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var reviews = new Mock<IShareReviewService>();
        reviews.Setup(service => service.GetReviewerReviewAsync(folderId, callerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShareReviewSummaryDto(folderId, "Folder", 0, 0, 0, 0, 0, 1, Array.Empty<ShareReviewFileDto>()));
        var controller = new FoldersController(Mock.Of<IFolderService>(), reviews.Object, NullLogger<FoldersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(callerId) } }
        };

        var result = await controller.GetReviewerShareReview(folderId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var authorize = typeof(FoldersController).GetMethod(nameof(FoldersController.GetReviewerShareReview))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        authorize.Roles.Should().Be("Admin,Moderator");
    }

    [Test]
    public async Task GetPendingShareReviewQueue_ForwardsSubjectAndMapsServiceErrors()
    {
        var callerId = Guid.NewGuid();
        var item = new PendingShareFolderDto(
            Guid.NewGuid(), "Folder", "Owner", "SWP391", "SU26", 1, DateTimeOffset.UtcNow,
            1, 0, null, null, null, Array.Empty<PendingShareDocumentDto>());
        var reviews = new Mock<IShareReviewService>();
        reviews.Setup(service => service.GetPendingReviewerQueueAsync(callerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item });
        var controller = new FoldersController(Mock.Of<IFolderService>(), reviews.Object, NullLogger<FoldersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(callerId, useSubInsteadOfNameId: true) } }
        };

        var result = await controller.GetPendingShareReviewQueue(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(new[] { item });
        var authorize = typeof(FoldersController).GetMethod(nameof(FoldersController.GetPendingShareReviewQueue))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        authorize.Roles.Should().Be("Admin,Moderator");

        reviews.Reset();
        reviews.Setup(service => service.GetPendingReviewerQueueAsync(callerId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AdminException(403, "share_reviewer_role_required", "forbidden"));

        var forbidden = await controller.GetPendingShareReviewQueue(CancellationToken.None);

        var error = forbidden.Result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        error.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("share_reviewer_role_required");
    }
}
