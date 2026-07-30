using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

/// <summary>
/// Sprint 1 SCRUM-13/15/25 endpoints. All endpoints require a valid Bearer JWT issued
/// by Supabase GoTrue; ownership of each row is enforced inside <see cref="IDocumentService"/>.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class DocumentsController : ControllerBase
{
    /// <summary>Absolute multipart cap mirrors <see cref="DocumentService.MaxFileSizeBytes"/>.</summary>
    public const long MaxRequestBodyBytes = DocumentService.MaxFileSizeBytes;

    private readonly IDocumentService _service;
    private readonly IDocumentIngestionService? _ingestionService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentService service,
        ILogger<DocumentsController> logger,
        IDocumentIngestionService? ingestionService = null)
    {
        _service = service;
        _logger = logger;
        _ingestionService = ingestionService;
    }

    /// <summary>SCRUM-13: multipart upload of a single document.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBodyBytes)]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<DocumentDto>> Upload(
        [FromForm] UploadDocumentRequest request,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "missing_file",
                Message = "Request must include a 'file' multipart part."
            });
        }

        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            await using var stream = file.OpenReadStream();
            var dto = await _service.UploadAsync(
                supabaseUserId,
                request,
                file.FileName,
                file.ContentType ?? "application/octet-stream",
                file.Length,
                stream,
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (PlanException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected document upload failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while uploading the document."
            });
        }
    }

    /// <summary>Manual Sprint 2 RAG re-ingestion endpoint for smoke/debug.</summary>
    [HttpPost("{id:guid}/ingest")]
    [ProducesResponseType(typeof(DocumentIngestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DocumentIngestionResult>> Ingest(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (_ingestionService is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse
            {
                Code = "ingestion_unavailable",
                Message = "Document ingestion service is not configured."
            });
        }

        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var result = await _ingestionService.IngestAsync(id, supabaseUserId, cancellationToken);
            return Ok(result);
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected document ingestion failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while ingesting the document."
            });
        }
    }

    /// <summary>SCRUM-15/25: list documents owned by the caller, optionally filtered.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DocumentDto>>> List(
        [FromQuery] DocumentListQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var rows = await _service.ListAsync(supabaseUserId, query, cancellationToken);
            return Ok(rows);
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected document list failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while listing documents."
            });
        }
    }

    /// <summary>Detail endpoint — returns the doc with a 5-minute signed download URL.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var dto = await _service.GetByIdAsync(supabaseUserId, id, cancellationToken);
            return Ok(dto);
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected document get failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while fetching the document."
            });
        }
    }

    /// <summary>Move a document into a folder, or back to loose documents.</summary>
    [HttpPut("{id:guid}/folder")]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDto>> MoveToFolder(
        Guid id,
        [FromBody] MoveDocumentFolderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var dto = await _service.MoveToFolderAsync(supabaseUserId, id, request.FolderId, cancellationToken);
            return Ok(dto);
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected document move failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while moving the document."
            });
        }
    }

    /// <summary>
    /// Returns a URL pointing to the Microsoft Office Online Viewer with the document
    /// embedded. The backend downloads the file from Supabase Storage and caches it
    /// locally so the viewer can fetch it without authentication.
    /// </summary>
    [HttpGet("{id:guid}/file")]
    [ProducesResponseType(typeof(FileViewUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileViewUrlResponse>> GetFileViewUrl(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var signedUrl = await _service.GetFileViewUrlAsync(supabaseUserId, id, cancellationToken);

            var officeViewerUrl = $"https://view.officeapps.live.com/op/embed.aspx?src={Uri.EscapeDataString(signedUrl)}";

            return Ok(new FileViewUrlResponse
            {
                Url = officeViewerUrl,
                DownloadUrl = signedUrl,
            });
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected document file URL generation failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while generating the document view URL."
            });
        }
    }

    /// <summary>Rename a document. Only the display FileName is updated; storage path stays unchanged.</summary>
    [HttpPut("{id:guid}/rename")]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDto>> Rename(
        Guid id,
        [FromBody] RenameDocumentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var dto = await _service.RenameAsync(supabaseUserId, id, request.FileName, cancellationToken);
            return Ok(dto);
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected document rename failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while renaming the document."
            });
        }
    }

    /// <summary>Returns the extracted text content (chunks) for a document.</summary>
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(typeof(DocumentContentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentContentDto>> GetContent(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var content = await _service.GetContentAsync(supabaseUserId, id, cancellationToken);
            return Ok(content);
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected document content fetch failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while fetching document content."
            });
        }
    }

    /// <summary>Hard delete — removes row, cascades chunks, deletes storage object.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            await _service.DeleteAsync(supabaseUserId, id, cancellationToken);
            return NoContent();
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected document delete failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while deleting the document."
            });
        }
    }

    private ObjectResult ToErrorResult(DocumentException exception) =>
        StatusCode(exception.StatusCode, new ApiErrorResponse
        {
            Code = exception.Code,
            Message = exception.Message,
        });

    private ObjectResult ToErrorResult(PlanException exception) =>
        StatusCode(exception.StatusCode, new ApiErrorResponse
        {
            Code = exception.Code,
            Message = exception.Message,
        });

    private Guid GetSupabaseUserIdFromClaims()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (Guid.TryParse(sub, out var id))
        {
            return id;
        }

        throw new DocumentException(401, "missing_user_id",
            "Authenticated Supabase user id is missing or invalid.");
    }
}
