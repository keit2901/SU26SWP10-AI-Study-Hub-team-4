using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

/// <summary>
/// Plan catalog and current-user quota endpoints. All endpoints require a valid Bearer JWT
/// issued by Supabase GoTrue.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PlansController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly IStorageQuotaService _quotaService;
    private readonly ILogger<PlansController> _logger;

    public PlansController(
        IPlanService planService,
        IStorageQuotaService quotaService,
        ILogger<PlansController> logger)
    {
        _planService = planService;
        _quotaService = quotaService;
        _logger = logger;
    }

    /// <summary>Returns all active plans, ordered by SortOrder.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public IActionResult GetPlans()
    {
        var plans = _planService.GetActivePlans()
            .Select(p => new PlanDto(
                p.PlanKey,
                p.DisplayName,
                p.Description,
                p.StorageQuotaBytes,
                p.MaxDocumentCount,
                p.MaxFolderCount,
                p.DailyTokenQuota,
                p.MaxFileSizeBytes,
                p.MaxDocsPerFolder,
                p.MonthlyPriceVnd,
                p.YearlyPriceVnd))
            .ToList();
        return Ok(plans);
    }

    /// <summary>Returns the calling user's plan and current storage usage vs quota.</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(StorageQuotaSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentPlan(CancellationToken ct)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var snapshot = await _quotaService.GetSnapshotAsync(supabaseUserId, ct);
            return Ok(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected plan snapshot failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while fetching the plan snapshot."
            });
        }
    }

    private Guid GetSupabaseUserIdFromClaims()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (Guid.TryParse(sub, out var id))
        {
            return id;
        }

        throw new InvalidOperationException("Authenticated Supabase user id is missing or invalid.");
    }
}
