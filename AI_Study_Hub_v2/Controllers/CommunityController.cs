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
public sealed class CommunityController : ControllerBase
{
    private readonly ICommunityService _communityService;
    private readonly ILogger<CommunityController> _logger;

    public CommunityController(ICommunityService communityService, ILogger<CommunityController> logger)
    {
        _communityService = communityService;
        _logger = logger;
    }

    [HttpPost("report")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Guid>> ReportFolder(
        [FromBody] CreateReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = await _communityService.ReportFolderAsync(
                GetSupabaseUserIdFromClaims(),
                request.FolderId,
                request.Reason,
                cancellationToken);
            return Ok(id);
        }
        catch (CommunityException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected report folder failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while submitting the report."
            });
        }
    }

    [HttpGet("reports/pending")]
    [Authorize(Roles = "Admin,Moderator")]
    [ProducesResponseType(typeof(IReadOnlyList<CommunityReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<CommunityReportDto>>> GetPendingReports(
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _communityService.GetPendingReportsAsync(cancellationToken));
        }
        catch (CommunityException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected get pending reports failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while fetching reports."
            });
        }
    }

    [HttpPatch("reports/{id:guid}/resolve")]
    [Authorize(Roles = "Admin,Moderator")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveReport(
        Guid id,
        [FromBody] ResolveReportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _communityService.ResolveReportAsync(
                GetSupabaseUserIdFromClaims(),
                id,
                request.Status,
                request.Resolution,
                cancellationToken);
            return Ok();
        }
        catch (CommunityException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected resolve report failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while resolving the report."
            });
        }
    }

    private ObjectResult ToErrorResult(CommunityException exception) =>
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

        throw new CommunityException(401, "missing_user_id",
            "Authenticated Supabase user id is missing or invalid.");
    }
}
