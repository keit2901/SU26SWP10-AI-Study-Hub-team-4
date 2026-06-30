using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/admin/audit-logs")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _auditLogs;

    public AuditLogsController(IAuditLogService auditLogs)
    {
        _auditLogs = auditLogs;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AuditLogDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AuditLogDto>>> List(
        [FromQuery] string? action,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
        => Ok(await _auditLogs.ListAsync(action, from, to, limit, cancellationToken));

    [HttpGet("export")]
    [Produces("text/csv")]
    public async Task<IActionResult> Export(
        [FromQuery] string? action,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var logs = await _auditLogs.ListAsync(action, from, to, limit: 10_000, cancellationToken);
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Timestamp,Action,Actor,Entity,Severity,Before,After");
        foreach (var log in logs)
        {
            csv.AppendLine($"\"{log.CreatedAt:O}\",\"{log.Action}\",\"{log.ActorName}\",\"{log.EntityType}/{log.EntityId}\",\"{log.Severity}\",\"{EscapeCsv(log.BeforeJson)}\",\"{EscapeCsv(log.AfterJson)}\"");
        }
        var fileName = $"audit-logs-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
    }

    private static string EscapeCsv(string? value)
        => value?.Replace("\"", "\"\"") ?? string.Empty;
}
