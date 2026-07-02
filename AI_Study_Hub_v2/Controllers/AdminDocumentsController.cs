using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/admin/documents")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class AdminDocumentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDocumentIngestionService _ingestionService;
    private readonly string _chunkingStrategy;
    private readonly ILogger<AdminDocumentsController> _logger;

    public AdminDocumentsController(
        AppDbContext db,
        IDocumentIngestionService ingestionService,
        Microsoft.Extensions.Options.IOptions<AI_Study_Hub_v2.Options.RagOptions> ragOptions,
        ILogger<AdminDocumentsController> logger)
    {
        _db = db;
        _ingestionService = ingestionService;
        _chunkingStrategy = ragOptions.Value.ChunkingStrategy;
        _logger = logger;
    }

    [HttpPost("reingest-all")]
    public async Task<ActionResult<ReingestAllDocumentsResponse>> ReingestAll(
        CancellationToken cancellationToken)
    {
        var documents = await _db.Documents
            .Include(d => d.User)
            .Where(d => d.Status == DocumentStatus.Ready || d.Status == DocumentStatus.Failed)
            .OrderBy(d => d.Status == DocumentStatus.Failed ? 0 : 1)
            .ThenBy(d => d.CreatedAt)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.User.SupabaseUserId
            })
            .ToListAsync(cancellationToken);

        var succeeded = 0;
        var failed = 0;
        var failures = new List<ReingestDocumentFailure>();

        foreach (var document in documents)
        {
            try
            {
                var result = await _ingestionService.IngestAsync(
                    document.Id,
                    document.SupabaseUserId,
                    cancellationToken);

                if (result.Success)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                    failures.Add(new ReingestDocumentFailure(
                        document.Id,
                        document.FileName,
                        result.ErrorMessage ?? "Re-ingestion failed."));
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                failures.Add(new ReingestDocumentFailure(
                    document.Id,
                    document.FileName,
                    ex.Message));

                _logger.LogWarning(
                    ex,
                    "Failed to re-ingest document {DocumentId}.",
                    document.Id);
            }
        }

        return Ok(new ReingestAllDocumentsResponse(
            Total: documents.Count,
            Succeeded: succeeded,
            Failed: failed,
            ChunkingStrategy: _chunkingStrategy,
            Failures: failures));
    }
}

public sealed record ReingestAllDocumentsResponse(
    int Total,
    int Succeeded,
    int Failed,
    string ChunkingStrategy,
    IReadOnlyList<ReingestDocumentFailure> Failures);

public sealed record ReingestDocumentFailure(
    Guid DocumentId,
    string FileName,
    string ErrorMessage);
