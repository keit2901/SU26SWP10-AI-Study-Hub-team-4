using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services.Rag.Benchmarking;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/benchmark")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BenchmarkController : ControllerBase
{
    private readonly BenchmarkRunner _runner;
    private readonly ChunkingBenchmarkService _chunkingBenchmarkService;
    private readonly ILogger<BenchmarkController> _logger;

    public BenchmarkController(
        BenchmarkRunner runner,
        ChunkingBenchmarkService chunkingBenchmarkService,
        ILogger<BenchmarkController> logger)
    {
        _runner = runner;
        _chunkingBenchmarkService = chunkingBenchmarkService;
        _logger = logger;
    }

    [HttpPost("run")]
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
            request.ModelName ?? "llama-3.3-70b-versatile",
            Count: request.Count,
            DocumentIds: request.DocumentIds);

        var result = await _runner.RunAsync(supabaseUserId.Value, config, cancellationToken: cancellationToken);

        _logger.LogInformation("Manual benchmark run completed: {Model}", result.ModelName);
        return Ok(result);
    }

    [HttpPost("chunking-compare")]
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
