using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public class FoldersControllerTests
{
    private static FoldersController BuildSut(IFolderService service, ClaimsPrincipal? user = null)
    {
        var ctrl = new FoldersController(service, NullLogger<FoldersController>.Instance);
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
}
