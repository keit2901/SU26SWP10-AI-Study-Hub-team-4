using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("user/stats")]
    public async Task<ActionResult<UserDashboardStatsDto>> GetUserStats(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var stats = await _dashboardService.GetUserStatsAsync(userId, ct);
        return Ok(stats);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin/stats")]
    public async Task<ActionResult<AdminDashboardStatsDto>> GetAdminStats(CancellationToken ct)
    {
        var stats = await _dashboardService.GetAdminStatsAsync(ct);
        return Ok(stats);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin/activity-trends")]
    public async Task<ActionResult<ActivityTrendsDto>> GetActivityTrends(
        [FromQuery] string period = "day",
        CancellationToken ct = default)
    {
        var trends = await _dashboardService.GetActivityTrendsAsync(period, ct);
        return Ok(trends);
    }

    [HttpGet("moderation/documents")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<List<DocumentDto>>> GetPendingModerationDocuments(
        [FromQuery] Guid? folderId, CancellationToken ct)
    {
        if (!TryGetSupabaseUserId(out var supabaseUserId)) return Unauthorized();
        try
        {
            return Ok(await _dashboardService.GetPendingModerationDocumentsAsync(supabaseUserId, folderId, ct));
        }
        catch (DashboardModerationException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
    }

    [HttpGet("moderation/analytics")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<UserAnalyticsDto>> GetModerationAnalytics(
        [FromQuery] Guid? folderId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        if (!TryGetSupabaseUserId(out var supabaseUserId)) return Unauthorized();
        try
        {
            return Ok(await _dashboardService.GetModerationAnalyticsAsync(supabaseUserId, folderId, page, pageSize, ct));
        }
        catch (DashboardModerationException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
    }

    [HttpGet("moderation/documents/{documentId:guid}/signed-url")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<string>> GetModerationDocumentSignedUrl(Guid documentId, CancellationToken ct)
    {
        if (!TryGetSupabaseUserId(out var supabaseUserId)) return Unauthorized();
        try
        {
            var url = await _dashboardService.GetModerationDocumentSignedUrlAsync(supabaseUserId, documentId, ct);
            return url is null
                ? NotFound(new ApiErrorResponse { Code = "signed_url_not_found", Message = "Signed URL could not be created." })
                : Ok(url);
        }
        catch (DashboardModerationException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
    }

    [HttpPost("moderation/documents/{documentId:guid}/approve")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<IActionResult> ApproveDocument(Guid documentId, CancellationToken ct)
    {
        if (!TryGetSupabaseUserId(out var supabaseUserId)) return Unauthorized();
        try
        {
            await _dashboardService.ApproveDocumentAsync(supabaseUserId, documentId, ct);
            return NoContent();
        }
        catch (DashboardModerationException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
    }

    [HttpPost("moderation/documents/{documentId:guid}/ai-review")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<DocumentAiReviewResultDto>> AiReviewDocument(Guid documentId, CancellationToken ct)
    {
        if (!TryGetSupabaseUserId(out var supabaseUserId)) return Unauthorized();
        try
        {
            var result = await _dashboardService.AiReviewDocumentAsync(supabaseUserId, documentId, ct);
            return Ok(result);
        }
        catch (DashboardModerationException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
    }

    [HttpPost("moderation/documents/{documentId:guid}/reject")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<IActionResult> RejectDocument(
        Guid documentId, [FromBody] DocumentModerationRejectRequest? request, CancellationToken ct)
    {
        if (!TryGetSupabaseUserId(out var supabaseUserId)) return Unauthorized();
        try
        {
            await _dashboardService.RejectDocumentAsync(supabaseUserId, documentId, request?.Reason, ct);
            return NoContent();
        }
        catch (DashboardModerationException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
    }

    private bool TryGetSupabaseUserId(out Guid supabaseUserId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out supabaseUserId);
    }
}

public sealed class DocumentModerationRejectRequest
{
    [System.ComponentModel.DataAnnotations.StringLength(2000)]
    public string? Reason { get; set; }
}
