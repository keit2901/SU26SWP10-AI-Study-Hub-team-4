using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/admin/moderation")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin,Moderator")]
public sealed class DocumentModerationController : ControllerBase
{
    private readonly IDocumentModerationService _moderation;
    private readonly IAuditLogService _audit;
    private readonly ILogger<DocumentModerationController> _logger;

    public DocumentModerationController(
        IDocumentModerationService moderation,
        IAuditLogService audit,
        ILogger<DocumentModerationController> logger)
    {
        _moderation = moderation;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet("queue")]
    public async Task<ActionResult<IReadOnlyList<ModerationQueueDocumentDto>>> GetQueue(CancellationToken ct)
    {
        var docs = await _moderation.GetQueueAsync(ct);
        return Ok(docs);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("escalated")]
    public async Task<ActionResult<IReadOnlyList<ModerationQueueDocumentDto>>> GetEscalated(CancellationToken ct)
    {
        var docs = await _moderation.GetEscalatedQueueAsync(ct);
        return Ok(docs);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult> Approve(Guid id, CancellationToken ct)
    {
        var result = await _moderation.ApproveAsync(id, ct);
        if (result)
        {
            _audit.Add(GetActorUserId(), "MODERATION_APPROVE", "documents", id.ToString(), "Medium",
                null, JsonSerializer.Serialize(new { status = "Approved" }),
                null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.TraceIdentifier);
        }
        return result ? Ok() : NotFound();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult> Reject(Guid id, [FromBody] ModerationRejectRequest request, CancellationToken ct)
    {
        var result = await _moderation.RejectAsync(id, request.Reason, ct);
        if (result)
        {
            _audit.Add(GetActorUserId(), "MODERATION_REJECT", "documents", id.ToString(), "Medium",
                null, JsonSerializer.Serialize(new { reason = request.Reason }),
                null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.TraceIdentifier);
        }
        return result ? Ok() : NotFound();
    }

    [HttpPost("{id:guid}/escalate")]
    public async Task<ActionResult> Escalate(Guid id, CancellationToken ct)
    {
        var result = await _moderation.EscalateAsync(id, ct);
        if (result)
        {
            _audit.Add(GetActorUserId(), "MODERATION_ESCALATE", "documents", id.ToString(), "High",
                null, null, null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.TraceIdentifier);
        }
        return result ? Ok() : NotFound();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult> Restore(Guid id, CancellationToken ct)
    {
        var result = await _moderation.RestoreAsync(id, ct);
        if (result)
        {
            _audit.Add(GetActorUserId(), "MODERATION_RESTORE", "documents", id.ToString(), "Medium",
                null, null, null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.TraceIdentifier);
        }
        return result ? Ok() : NotFound();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _moderation.DeleteAsync(id, ct);
        if (result)
        {
            _audit.Add(GetActorUserId(), "MODERATION_DELETE", "documents", id.ToString(), "High",
                null, null, null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.TraceIdentifier);
        }
        return result ? NoContent() : NotFound();
    }

    private Guid GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}

public sealed class ModerationRejectRequest
{
    public string? Reason { get; set; }
}
