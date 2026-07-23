using System.Security.Claims;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/admin/escalations")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin,Moderator")]
public sealed class EscalationController : ControllerBase
{
    private readonly IEscalationService _escalation;
    private readonly AppDbContext _db;
    private readonly ILogger<EscalationController> _logger;

    public EscalationController(IEscalationService escalation, AppDbContext db, ILogger<EscalationController> logger)
    {
        _escalation = escalation;
        _db = db;
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
            var userId = await GetLocalUserIdAsync();
            if (userId is null)
                return Unauthorized();

            var result = await _escalation.CreateAsync(userId.Value, request, cancellationToken);
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

    [HttpGet("all")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentEscalationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentEscalationDto>>> GetAll(CancellationToken cancellationToken)
        => Ok(await _escalation.GetAllAsync(cancellationToken));

    [HttpGet("my")]
    [Authorize(Roles = "Admin,Moderator")]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentEscalationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentEscalationDto>>> GetMy(CancellationToken cancellationToken)
    {
        var userId = await GetLocalUserIdAsync();
        if (userId is null)
            return Unauthorized();

        return Ok(await _escalation.GetMyAsync(userId.Value, cancellationToken));
    }

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
            var userId = await GetLocalUserIdAsync();
            if (userId is null)
                return Unauthorized();

            var result = await _escalation.ResolveAsync(id, request, userId.Value, cancellationToken);
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

    private async Task<Guid?> GetLocalUserIdAsync()
    {
        var supabaseUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        if (supabaseUserIdClaim is null || !Guid.TryParse(supabaseUserIdClaim.Value, out var supabaseUserId))
            return null;

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);
        return user?.Id;
    }
}
