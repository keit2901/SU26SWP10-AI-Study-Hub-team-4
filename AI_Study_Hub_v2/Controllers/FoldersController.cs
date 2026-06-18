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
    private readonly ILogger<FoldersController> _logger;

    public FoldersController(IFolderService service, ILogger<FoldersController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FolderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<FolderDto>>> List(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.ListAsync(GetSupabaseUserIdFromClaims(), cancellationToken));

    [HttpGet("shared")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<FolderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FolderDto>>> ListShared(CancellationToken cancellationToken)
        => Ok(await _service.ListSharedAsync(cancellationToken));

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

    [HttpPatch("{id:guid}/share")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> ToggleShare(Guid id, CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.ToggleShareAsync(GetSupabaseUserIdFromClaims(), id, cancellationToken));

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
