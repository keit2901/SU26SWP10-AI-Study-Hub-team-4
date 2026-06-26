using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

/// <summary>
/// D5 SCRUM-28 controller-level coverage cho <see cref="DocumentsController"/>: focus vào
/// (1) đọc Supabase user id từ <c>sub</c> / <see cref="ClaimTypes.NameIdentifier"/> claim,
/// (2) map <see cref="DocumentException"/> → <see cref="ApiErrorResponse"/> + status,
/// (3) các nhánh controller xử lý trước khi gọi service (missing file, invalid sub claim).
/// Service được mock — flow business logic đã có coverage riêng ở <see cref="Services.DocumentServiceTests"/>.
/// </summary>
[TestFixture]
public class DocumentsControllerTests
{
    // -------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------

    private static DocumentsController BuildSut(IDocumentService service, ClaimsPrincipal? user = null)
    {
        var ctrl = new DocumentsController(service, NullLogger<DocumentsController>.Instance);
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
            // The controller checks ClaimTypes.NameIdentifier first, then falls back to "sub".
            // Cover both paths by parametrising which claim we attach.
            claims.Add(new Claim(
                useSubInsteadOfNameId ? "sub" : ClaimTypes.NameIdentifier,
                supabaseUserId.Value.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    private static IFormFile FormFile(string fileName = "x.pdf", string mime = "application/pdf", byte[]? bytes = null)
    {
        var content = bytes ?? new byte[16];
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, name: "file", fileName: fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = mime,
        };
    }

    private static DocumentDto SampleDto(Guid? id = null, string? signedUrl = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        FileName = "x.pdf",
        FileSizeBytes = 16,
        MimeType = "application/pdf",
        SubjectCode = "SWP391",
        Semester = "SU26",
        Status = DocumentStatus.Ready,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        DownloadUrl = signedUrl,
    };

    // -------------------------------------------------------------------------
    // Upload
    // -------------------------------------------------------------------------

    [Test]
    public async Task Upload_HappyPath_Returns201_AndForwardsSubClaimToService()
    {
        var supabaseUserId = Guid.NewGuid();
        var dto = SampleDto();
        Guid? captured = null;

        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.UploadAsync(
                It.IsAny<Guid>(),
                It.IsAny<UploadDocumentRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, UploadDocumentRequest, string, string, long, Stream, CancellationToken>(
                (uid, _, _, _, _, _, _) => captured = uid)
            .ReturnsAsync(dto);

        var sut = BuildSut(svc.Object, Principal(supabaseUserId));

        var result = await sut.Upload(
            new UploadDocumentRequest { SubjectCode = "SWP391", Semester = "SU26" },
            FormFile(),
            CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.ActionName.Should().Be(nameof(DocumentsController.GetById));
        created.RouteValues!["id"].Should().Be(dto.Id);
        created.Value.Should().BeSameAs(dto);
        captured.Should().Be(supabaseUserId);
    }

    [Test]
    public async Task Upload_FallsBackToSubClaim_WhenNameIdentifierMissing()
    {
        var supabaseUserId = Guid.NewGuid();
        Guid? captured = null;

        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.UploadAsync(It.IsAny<Guid>(), It.IsAny<UploadDocumentRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UploadDocumentRequest, string, string, long, Stream, CancellationToken>(
                (uid, _, _, _, _, _, _) => captured = uid)
            .ReturnsAsync(SampleDto());

        var sut = BuildSut(svc.Object, Principal(supabaseUserId, useSubInsteadOfNameId: true));

        await sut.Upload(
            new UploadDocumentRequest { SubjectCode = "SWP391", Semester = "SU26" },
            FormFile(),
            CancellationToken.None);

        captured.Should().Be(supabaseUserId);
    }

    [Test]
    public async Task Upload_NoFile_Returns400_MissingFile_AndDoesNotCallService()
    {
        var svc = new Mock<IDocumentService>(MockBehavior.Strict);
        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Upload(
            new UploadDocumentRequest { SubjectCode = "SWP391", Semester = "SU26" },
            file: null!,
            CancellationToken.None);

        var bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.StatusCode.Should().Be(400);
        bad.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("missing_file");
    }

    [Test]
    public async Task Upload_EmptyFile_Returns400_MissingFile()
    {
        var svc = new Mock<IDocumentService>(MockBehavior.Strict);
        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Upload(
            new UploadDocumentRequest { SubjectCode = "SWP391", Semester = "SU26" },
            FormFile(bytes: Array.Empty<byte>()),
            CancellationToken.None);

        var bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("missing_file");
    }

    [Test]
    public async Task Upload_InvalidSubClaim_Returns401_MissingUserId()
    {
        var svc = new Mock<IDocumentService>(MockBehavior.Strict); // strict → must NOT be called
        // Principal with a non-GUID sub claim simulates a malformed JWT.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "not-a-guid") },
            authenticationType: "Bearer"));
        var sut = BuildSut(svc.Object, principal);

        var result = await sut.Upload(
            new UploadDocumentRequest { SubjectCode = "SWP391", Semester = "SU26" },
            FormFile(),
            CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(401);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("missing_user_id");
    }

    [TestCase(404, "user_not_found")]
    [TestCase(403, "user_inactive")]
    [TestCase(413, "file_too_large")]
    [TestCase(415, "unsupported_media_type")]
    [TestCase(404, "folder_not_found")]
    [TestCase(503, "storage_unavailable")]
    [TestCase(500, "upload_persist_failed")]
    public async Task Upload_WhenServiceThrowsDocumentException_MapsStatusAndCode(int status, string code)
    {
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.UploadAsync(It.IsAny<Guid>(), It.IsAny<UploadDocumentRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentException(status, code, "msg"));

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Upload(
            new UploadDocumentRequest { SubjectCode = "SWP391", Semester = "SU26" },
            FormFile(),
            CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(status);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be(code);
    }

    [Test]
    public async Task Upload_WhenServiceThrowsUnexpected_Returns500_UnexpectedError()
    {
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.UploadAsync(It.IsAny<Guid>(), It.IsAny<UploadDocumentRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kaboom"));

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Upload(
            new UploadDocumentRequest { SubjectCode = "SWP391", Semester = "SU26" },
            FormFile(),
            CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(500);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("unexpected_error");
    }

    // -------------------------------------------------------------------------
    // List
    // -------------------------------------------------------------------------

    [Test]
    public async Task List_HappyPath_Returns200_AndPassesQueryThrough()
    {
        var supabaseUserId = Guid.NewGuid();
        var rows = new List<DocumentDto> { SampleDto(), SampleDto() };
        DocumentListQuery? capturedQuery = null;

        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.ListAsync(supabaseUserId, It.IsAny<DocumentListQuery>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DocumentListQuery, CancellationToken>((_, q, _) => capturedQuery = q)
            .ReturnsAsync(rows);

        var sut = BuildSut(svc.Object, Principal(supabaseUserId));

        var query = new DocumentListQuery { SubjectCode = "SWP391", Semester = "SU26" };
        var result = await sut.List(query, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(rows);
        capturedQuery.Should().BeSameAs(query);
    }

    [Test]
    public async Task List_WhenServiceThrowsDocumentException_MapsStatusAndCode()
    {
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.ListAsync(It.IsAny<Guid>(), It.IsAny<DocumentListQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentException(404, "user_not_found", "no profile"));

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.List(new DocumentListQuery(), CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(404);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("user_not_found");
    }

    [Test]
    public async Task List_WhenServiceThrowsUnexpected_Returns500()
    {
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.ListAsync(It.IsAny<Guid>(), It.IsAny<DocumentListQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kaboom"));

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.List(new DocumentListQuery(), CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(500);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("unexpected_error");
    }

    // -------------------------------------------------------------------------
    // GetById
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetById_HappyPath_Returns200_WithDtoIncludingSignedUrl()
    {
        var dto = SampleDto(signedUrl: "https://storage/local/signed?token=xyz");
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), dto.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.GetById(dto.Id, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(dto);
        ((DocumentDto)ok.Value!).DownloadUrl.Should().StartWith("https://storage");
    }

    [Test]
    public async Task GetById_NotFound_Maps404_DocumentNotFound()
    {
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentException(404, "document_not_found", "missing"));

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.GetById(Guid.NewGuid(), CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(404);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("document_not_found");
    }

    // -------------------------------------------------------------------------
    // MoveToFolder
    // -------------------------------------------------------------------------

    [Test]
    public async Task MoveToFolder_HappyPath_Returns200_AndPassesFolderId()
    {
        var supabaseUserId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var dto = SampleDto(documentId);
        dto.FolderId = folderId;
        Guid? capturedUser = null;
        Guid? capturedDoc = null;
        Guid? capturedFolder = null;

        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.MoveToFolderAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, Guid?, CancellationToken>((uid, did, fid, _) =>
            {
                capturedUser = uid;
                capturedDoc = did;
                capturedFolder = fid;
            })
            .ReturnsAsync(dto);

        var sut = BuildSut(svc.Object, Principal(supabaseUserId));

        var result = await sut.MoveToFolder(
            documentId,
            new MoveDocumentFolderRequest { FolderId = folderId },
            CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(dto);
        capturedUser.Should().Be(supabaseUserId);
        capturedDoc.Should().Be(documentId);
        capturedFolder.Should().Be(folderId);
    }

    [Test]
    public async Task MoveToFolder_FolderNotFound_Returns404()
    {
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.MoveToFolderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentException(404, "folder_not_found", "missing"));

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.MoveToFolder(Guid.NewGuid(), new MoveDocumentFolderRequest { FolderId = Guid.NewGuid() }, CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(404);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("folder_not_found");
    }

    // -------------------------------------------------------------------------
    // Delete
    // -------------------------------------------------------------------------

    [Test]
    public async Task Delete_HappyPath_Returns204_NoContent()
    {
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Delete(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Test]
    public async Task Delete_DocumentNotFound_Returns404()
    {
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentException(404, "document_not_found", "missing"));

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Delete(Guid.NewGuid(), CancellationToken.None);

        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(404);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("document_not_found");
    }

    [Test]
    public async Task Delete_WhenServiceThrowsUnexpected_Returns500()
    {
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kaboom"));

        var sut = BuildSut(svc.Object, Principal(Guid.NewGuid()));

        var result = await sut.Delete(Guid.NewGuid(), CancellationToken.None);

        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(500);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("unexpected_error");
    }
}
