using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/admin/settings")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class SystemSettingsController : ControllerBase
{
    private readonly ISystemConfigService _config;
    private readonly ILogger<SystemSettingsController> _logger;

    public SystemSettingsController(ISystemConfigService config, ILogger<SystemSettingsController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SystemConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SystemConfigDto>>> GetAll(CancellationToken cancellationToken)
        => Ok(await _config.GetAllAsync(cancellationToken));

    [HttpPut("{key}")]
    [ProducesResponseType(typeof(SystemConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SystemConfigDto>> Update(
        string key,
        [FromBody] UpdateSystemConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var adminEmail = User.FindFirstValue(ClaimTypes.Email) ?? "admin";
            var result = await _config.UpdateValueAsync(key, request.Value, adminEmail, cancellationToken);
            return Ok(result);
        }
        catch (AdminException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected system config update failure for key {ConfigKey}.", key);
            return StatusCode(500, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while updating the system configuration."
            });
        }
    }

    private Guid GetSupabaseUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                    ?? User.FindFirst("sub");
        if (claim is not null && Guid.TryParse(claim.Value, out var id))
        {
            return id;
        }
        throw new AdminException(401, "invalid_token", "Valid user identity is required.");
    }
}
