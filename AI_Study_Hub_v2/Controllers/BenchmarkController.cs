using System.Security.Claims;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag.Benchmarking;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/benchmark")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BenchmarkController : ControllerBase
{
    private readonly BenchmarkRunner _runner;
    private readonly ChunkingBenchmarkService _chunkingBenchmarkService;
    private readonly AppDbContext _db;
    private readonly GroqOptions _groqOptions;
    private readonly ILogger<BenchmarkController> _logger;

    public BenchmarkController(
        BenchmarkRunner runner,
        ChunkingBenchmarkService chunkingBenchmarkService,
        AppDbContext db,
        IOptions<GroqOptions> groqOptions,
        ILogger<BenchmarkController> logger)
    {
        _runner = runner;
        _chunkingBenchmarkService = chunkingBenchmarkService;
        _db = db;
        _groqOptions = groqOptions.Value;
        _logger = logger;
    }

    [HttpPost("run")]
    [Authorize(Roles = Role.AdminRoleName, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(BenchmarkResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<BenchmarkResult>> Run(
        [FromBody] BenchmarkRunRequest request,
        CancellationToken cancellationToken)
    {
        var supabaseUserId = GetSupabaseUserId();
        if (supabaseUserId is null)
        {
            return Unauthorized(new ApiErrorResponse { Code = "missing_user", Message = "User not identified." });
        }

        var config = new BenchmarkConfig(
            string.IsNullOrWhiteSpace(request.ModelName) ? _groqOptions.Model : request.ModelName,
            Count: request.Count,
            DocumentIds: request.DocumentIds);

        var result = await _runner.RunAsync(supabaseUserId.Value, config, cancellationToken: cancellationToken);

        _logger.LogInformation("Manual benchmark run completed: {Model}", result.ModelName);
        return Ok(result);
    }

    [HttpGet("history")]
    [Authorize(Roles = Role.AdminRoleName, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(IReadOnlyList<BenchmarkHistoryItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BenchmarkHistoryItemDto>>> History(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var items = await _db.BenchmarkRuns
            .AsNoTracking()
            .OrderByDescending(x => x.RunAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(x => new BenchmarkHistoryItemDto(
                x.Id,
                x.ModelName,
                x.Provider,
                x.RunAt,
                x.OverallScore,
                x.CitationAccuracy,
                x.HallucinationRate,
                x.RefusalAccuracy,
                x.TutoringQuality,
                x.DiagramAccuracy,
                x.P50LatencyMs,
                x.P95LatencyMs,
                x.TotalQuestions,
                x.PassedQuestions,
                x.FailedQuestions,
                x.IsAutomated,
                x.AlertTriggered))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("automation/run-now")]
    [Authorize(Roles = Role.AdminRoleName, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(BenchmarkResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<BenchmarkResult>> RunAutomationNow(CancellationToken cancellationToken)
    {
        var supabaseUserId = GetSupabaseUserId();
        if (supabaseUserId is null)
        {
            return Unauthorized(new ApiErrorResponse { Code = "missing_user", Message = "User not identified." });
        }

        var result = await _runner.RunAsync(
            supabaseUserId.Value,
            new BenchmarkConfig(
                _groqOptions.Model,
                Count: null,
                DocumentIds: null,
                IsAutomated: true),
            cancellationToken: cancellationToken);

        return Ok(result);
    }

    [HttpPost("chunking-compare")]
    [Authorize(Roles = Role.AdminRoleName, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(ChunkingBenchmarkComparisonResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChunkingBenchmarkComparisonResult>> CompareChunking(
        [FromBody] ChunkingBenchmarkRunRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _chunkingBenchmarkService.RunAsync(
            request?.TopK,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Chunking benchmark completed: semantic recall@{TopK}={SemanticRecall:P2}, fixed recall@{TopK}={FixedRecall:P2}",
            result.TopK,
            result.Semantic.RecallAtK,
            result.TopK,
            result.Fixed.RecallAtK);

        return Ok(result);
    }

    private Guid? GetSupabaseUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        if (Guid.TryParse(sub, out var id))
        {
            return id;
        }
        return null;
    }
}

public sealed record BenchmarkRunRequest(
    string? ModelName = null,
    IReadOnlyList<Guid>? DocumentIds = null,
    int? Count = null);

public sealed record ChunkingBenchmarkRunRequest(
    int? TopK = null);
