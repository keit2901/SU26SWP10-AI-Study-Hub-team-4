using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
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

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminDocumentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminDocumentDto>>> List(
        [FromQuery] string? status,
        [FromQuery] string? subject,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Documents
            .Include(d => d.User)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status.ToString() == status);
        if (!string.IsNullOrWhiteSpace(subject))
            query = query.Where(d => d.SubjectCode == subject.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(d => d.FileName.Contains(q) || d.SubjectCode.Contains(q));

        var result = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * Math.Clamp(size, 1, 100))
            .Take(Math.Clamp(size, 1, 100))
            .Select(d => new AdminDocumentDto(
                d.Id,
                d.FileName,
                d.SubjectCode,
                d.User.FullName,
                d.User.Username ?? "",
                d.Status.ToString(),
                d.ReviewStatus.ToString(),
                d.MimeType,
                d.FileSizeBytes,
                d.StoragePath,
                d.Chunks.Count,
                d.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var doc = await _db.Documents.FindAsync(new object[] { id }, cancellationToken);
        if (doc is null)
            return NotFound(new ApiErrorResponse { Code = "document_not_found", Message = "Document not found." });

        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Admin deleted document {DocumentId} ({FileName})", id, doc.FileName);
        return NoContent();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminDocumentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminDocumentDetailDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var doc = await _db.Documents
            .Include(d => d.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (doc is null)
            return NotFound(new ApiErrorResponse { Code = "document_not_found", Message = "Document not found." });

        var chunkCount = await _db.DocumentChunks
            .CountAsync(c => c.DocumentId == id, cancellationToken);

        var chunkPreviews = await _db.DocumentChunks
            .Where(c => c.DocumentId == id)
            .OrderBy(c => c.ChunkIndex)
            .Take(20)
            .Select(c => new DocumentChunkPreviewDto(
                c.ChunkIndex,
                c.Content.Length > 200 ? c.Content.Substring(0, 200) + "..." : c.Content,
                (int)Math.Ceiling(c.Content.Length / 4.0),
                c.PageNumber))
            .ToListAsync(cancellationToken);

        return Ok(new AdminDocumentDetailDto(
            doc.Id,
            doc.FileName,
            doc.SubjectCode,
            doc.User.FullName,
            doc.User.Username ?? "",
            doc.Status.ToString(),
            doc.ReviewStatus.ToString(),
            doc.MimeType,
            doc.FileSizeBytes,
            doc.StoragePath,
            chunkCount,
            doc.PageCount,
            doc.ErrorMessage,
            doc.Semester,
            doc.CreatedAt,
            doc.UpdatedAt,
            chunkPreviews));
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
