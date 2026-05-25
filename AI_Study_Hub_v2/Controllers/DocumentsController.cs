using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
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
    /// <summary>50 MB cap mirrors <see cref="DocumentService.MaxFileSizeBytes"/>.</summary>
    public const long MaxRequestBodyBytes = 50L * 1024 * 1024;

    private readonly IDocumentService _service;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IDocumentService service, ILogger<DocumentsController> logger)
    {
        _service = service;
        _logger = logger;
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
