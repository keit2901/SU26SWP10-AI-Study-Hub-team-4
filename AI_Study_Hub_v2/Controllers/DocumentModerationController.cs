using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/admin/moderation")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin,Moderator")]
public sealed class DocumentModerationController : ControllerBase
{
    private readonly IDocumentModerationService _moderation;
    private readonly ILogger<DocumentModerationController> _logger;

    public DocumentModerationController(IDocumentModerationService moderation, ILogger<DocumentModerationController> logger)
    {
        _moderation = moderation;
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
        return result ? Ok() : NotFound();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult> Reject(Guid id, [FromBody] ModerationRejectRequest request, CancellationToken ct)
    {
        var result = await _moderation.RejectAsync(id, request.Reason, ct);
        return result ? Ok() : NotFound();
    }

    [HttpPost("{id:guid}/escalate")]
    public async Task<ActionResult> Escalate(Guid id, CancellationToken ct)
    {
        var result = await _moderation.EscalateAsync(id, ct);
        return result ? Ok() : NotFound();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult> Restore(Guid id, CancellationToken ct)
    {
        var result = await _moderation.RestoreAsync(id, ct);
        return result ? Ok() : NotFound();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _moderation.DeleteAsync(id, ct);
        return result ? NoContent() : NotFound();
    }
}

public sealed class ModerationRejectRequest
{
    public string? Reason { get; set; }
}
