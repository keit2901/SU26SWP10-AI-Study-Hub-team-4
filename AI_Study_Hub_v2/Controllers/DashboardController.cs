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
}
