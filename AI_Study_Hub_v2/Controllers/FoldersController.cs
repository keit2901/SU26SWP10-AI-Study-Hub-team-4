using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class FoldersController : ControllerBase
{
    private readonly IFolderService _service;
    private readonly IShareReviewService _shareReview;
    private readonly ILogger<FoldersController> _logger;

    public FoldersController(IFolderService service, IShareReviewService shareReview, ILogger<FoldersController> logger)
    {
        _service = service;
        _shareReview = shareReview;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FolderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<FolderDto>>> List(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.ListAsync(GetSupabaseUserIdFromClaims(), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> GetById(Guid id, CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.GetFolderAsync(GetSupabaseUserIdFromClaims(), id, cancellationToken));

    [HttpGet("shared")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<FolderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FolderDto>>> ListShared(CancellationToken cancellationToken)
        => await ExecuteAsync(() =>
            _service.ListSharedAsync(TryGetSupabaseUserIdFromClaims(), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FolderDto>> Create(
        [FromBody] CreateFolderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _service.CreateAsync(GetSupabaseUserIdFromClaims(), request, cancellationToken);
            return CreatedAtAction(nameof(List), new { id = dto.Id }, dto);
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
            _logger.LogError(ex, "Unexpected folder create failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while creating the folder."
            });
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FolderDto>> Update(
        Guid id,
        [FromBody] UpdateFolderRequest request,
        CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.UpdateAsync(GetSupabaseUserIdFromClaims(), id, request, cancellationToken));

    [HttpPatch("{id:guid}/favorite")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> ToggleFavorite(Guid id, CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.ToggleFavoriteAsync(GetSupabaseUserIdFromClaims(), id, cancellationToken));

    [HttpGet("personal-shared")]
    [ProducesResponseType(typeof(IReadOnlyList<FolderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<FolderDto>>> ListPersonalShared(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.ListPersonalSharedAsync(GetSupabaseUserIdFromClaims(), cancellationToken));

    [HttpPost("{id:guid}/copy")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> CopyShared(Guid id, CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.CopySharedFolderAsync(GetSupabaseUserIdFromClaims(), id, cancellationToken));

    [HttpPost("{id:guid}/vote")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> Vote(
        Guid id,
        [FromBody] VoteRequest request,
        CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.VoteAsync(GetSupabaseUserIdFromClaims(), id, request.IsLike, cancellationToken));

    [HttpPatch("{id:guid}/share")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> RequestShare(Guid id, CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.RequestShareAsync(GetSupabaseUserIdFromClaims(), id, cancellationToken));

    [HttpPost("{id:guid}/appeal-share-review")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> AppealShareReview(
        Guid id,
        [FromBody] AppealFolderShareRequest? request,
        CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.AppealShareReviewAsync(
            GetSupabaseUserIdFromClaims(),
            id,
            request ?? new AppealFolderShareRequest(),
            cancellationToken));

    [HttpPatch("{id:guid}/share/approve")]
    [Authorize(Roles = "Admin,Moderator")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> ApproveShare(Guid id, CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.ApproveFolderShareAsync(
            GetSupabaseUserIdFromClaims(), id, cancellationToken));

    [HttpPatch("{id:guid}/share/reject")]
    [Authorize(Roles = "Admin,Moderator")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> RejectShare(
        Guid id,
        [FromBody] RejectFolderShareRequest? request,
        CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.RejectFolderShareAsync(
            GetSupabaseUserIdFromClaims(),
            id,
            request ?? new RejectFolderShareRequest(),
            cancellationToken));

    [HttpPatch("{id:guid}/share/auto-check")]
    [Authorize(Roles = "Admin,Moderator")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> AutoCheckShare(Guid id, CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.AutoCheckFolderShareAsync(
            GetSupabaseUserIdFromClaims(), id, cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteAsync(GetSupabaseUserIdFromClaims(), id, cancellationToken);
            return NoContent();
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected folder delete failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while deleting the folder."
            });
        }
    }

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
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
            _logger.LogError(ex, "Unexpected folder operation failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while managing folders."
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

    private Guid? TryGetSupabaseUserIdFromClaims()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    // ── Share Review Endpoints ──

    [HttpGet("share-review/pending")]
    [Authorize(Roles = "Admin,Moderator")]
    [ProducesResponseType(typeof(IReadOnlyList<PendingShareFolderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PendingShareFolderDto>>> GetPendingShareReviewQueue(CancellationToken ct)
    {
        try
        {
            return Ok(await _shareReview.GetPendingReviewerQueueAsync(GetSupabaseUserIdFromClaims(), ct));
        }
        catch (AdminException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
    }

    [HttpGet("{folderId:guid}/share-review")]
    [ProducesResponseType(typeof(ShareReviewSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ShareReviewSummaryDto>> GetShareReview(Guid folderId, CancellationToken ct)
    {
        var userId = GetSupabaseUserIdFromClaims();
        var result = await _shareReview.GetReviewAsync(folderId, userId, ct);
        return Ok(result);
    }

    [HttpGet("{folderId:guid}/share-review/reviewer")]
    [Authorize(Roles = "Admin,Moderator")]
    [ProducesResponseType(typeof(ShareReviewSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShareReviewSummaryDto>> GetReviewerShareReview(Guid folderId, CancellationToken ct)
    {
        try
        {
            return Ok(await _shareReview.GetReviewerReviewAsync(
                folderId,
                GetSupabaseUserIdFromClaims(),
                ct));
        }
        catch (AdminException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
    }

    [HttpPost("{folderId:guid}/share-review/apply")]
    [ProducesResponseType(typeof(ApplyDecisionsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApplyDecisionsResponse>> ApplyShareReview(Guid folderId, [FromBody] ApplyDecisionsRequest req, CancellationToken ct)
    {
        var userId = GetSupabaseUserIdFromClaims();
        var result = await _shareReview.ApplyDecisionsAsync(folderId, userId, req, ct);
        return Ok(result);
    }

    [HttpPost("{folderId:guid}/share-review/retry")]
    public async Task<IActionResult> RetryShareReview(Guid folderId, CancellationToken ct)
    {
        var userId = GetSupabaseUserIdFromClaims();
        await _shareReview.RetryShareAfterResolveAsync(folderId, userId, ct);
        return Ok();
    }

    [HttpPost("{folderId:guid}/share-review/rollback")]
    [ProducesResponseType(typeof(ShareRollbackResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ShareRollbackResponse>> RollbackShare(Guid folderId, CancellationToken ct)
    {
        var userId = GetSupabaseUserIdFromClaims();
        var result = await _shareReview.TryRollbackShareAsync(folderId, userId, ct);
        return Ok(result);
    }
}
