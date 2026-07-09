using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/admin/escalations")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin,Moderator")]
public sealed class EscalationController : ControllerBase
{
    private readonly IEscalationService _escalation;
    private readonly ILogger<EscalationController> _logger;

    public EscalationController(IEscalationService escalation, ILogger<EscalationController> logger)
    {
        _escalation = escalation;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Moderator")]
    [ProducesResponseType(typeof(DocumentEscalationDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<DocumentEscalationDto>> Create(
        [FromBody] CreateEscalationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized();

            var result = await _escalation.CreateAsync(userId, request, cancellationToken);
            return CreatedAtAction(nameof(GetPending), null, result);
        }
        catch (AdminException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create escalation");
            return StatusCode(500, new ApiErrorResponse { Code = "unexpected_error", Message = "An unexpected error occurred." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentEscalationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentEscalationDto>>> GetPending(CancellationToken cancellationToken)
        => Ok(await _escalation.GetPendingAsync(cancellationToken));

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(DocumentEscalationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DocumentEscalationDto>> Resolve(
        Guid id,
        [FromBody] ResolveEscalationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized();

            var result = await _escalation.ResolveAsync(id, userId, request, cancellationToken);
            return Ok(result);
        }
        catch (AdminException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve escalation {EscalationId}", id);
            return StatusCode(500, new ApiErrorResponse { Code = "unexpected_error", Message = "An unexpected error occurred." });
        }
    }
}
