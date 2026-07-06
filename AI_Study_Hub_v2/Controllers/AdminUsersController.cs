using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/admin/users")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IAdminUserService _users;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(IAdminUserService users, ILogger<AdminUsersController> logger)
    {
        _users = users;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminUserDto>>> List(CancellationToken cancellationToken)
        => Ok(await _users.ListAsync(cancellationToken));

    [HttpPatch("{id:guid}/quota")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> UpdateQuota(
        Guid id,
        [FromBody] UpdateUserQuotaRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _users.UpdateQuotaAsync(
                GetSupabaseUserId(),
                id,
                request.DailyTokenQuota,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.TraceIdentifier,
                cancellationToken);
            return Ok(result);
        }
        catch (AdminException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected admin quota update failure for user {UserId}.", id);
            return StatusCode(500, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while updating the quota."
            });
        }
    }

    [HttpPatch("{id:guid}/role")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> UpdateRole(
        Guid id,
        [FromBody] UpdateUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _users.UpdateRoleAsync(
                GetSupabaseUserId(),
                id,
                request.Role,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.TraceIdentifier,
                cancellationToken);
            return Ok(result);
        }
        catch (AdminException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected admin role change failure for user {UserId} to {Role}.", id, request.Role);
            return StatusCode(500, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while changing the role."
            });
        }
    }

    [HttpPatch("{id:guid}/activate")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> ActivateUser(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _users.ToggleActiveAsync(
                GetSupabaseUserId(),
                id,
                activate: true,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.TraceIdentifier,
                cancellationToken);
            return Ok(result);
        }
        catch (AdminException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected admin activate failure for user {UserId}.", id);
            return StatusCode(500, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while activating the user."
            });
        }
    }

    [HttpPatch("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> DeactivateUser(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _users.ToggleActiveAsync(
                GetSupabaseUserId(),
                id,
                activate: false,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.TraceIdentifier,
                cancellationToken);
            return Ok(result);
        }
        catch (AdminException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected admin deactivate failure for user {UserId}.", id);
            return StatusCode(500, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while deactivating the user."
            });
        }
    }

    private Guid GetSupabaseUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(value, out var id))
        {
            return id;
        }
        throw new AdminException(401, "missing_user_id", "Authenticated user id is missing or invalid.");
    }
}
