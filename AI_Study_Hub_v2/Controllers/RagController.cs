using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/rag")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class RagController : ControllerBase
{
    private readonly IRagSearchService _searchService;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<RagController> _logger;

    public RagController(
        IRagSearchService searchService,
        Microsoft.Extensions.Options.IOptions<RagOptions> ragOptions,
        ILogger<RagController> logger)
    {
        _searchService = searchService;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    [HttpPost("search")]
    [ProducesResponseType(typeof(IReadOnlyList<RagSearchResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RagSearchResultDto>>> Search(
        [FromBody] RagSearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var results = await _searchService.SearchAsync(supabaseUserId, request, cancellationToken);
            return Ok(results);
        }
        catch (DocumentException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected RAG search failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while searching document chunks."
            });
        }
    }

    [HttpGet("scoring")]
    [ProducesResponseType(typeof(RagScoringInfoResponse), StatusCodes.Status200OK)]
    public ActionResult<RagScoringInfoResponse> Scoring()
    {
        return Ok(new RagScoringInfoResponse(
            _ragOptions.ChunkSizeChars,
            _ragOptions.ChunkOverlapChars,
            _ragOptions.EmbeddingDimensions,
            _ragOptions.DefaultTopK,
            _ragOptions.MaxTopK,
            "Lower score means closer semantic distance; results are ranked from most relevant to least relevant.",
            "Cosine distance over pgvector embeddings, constrained by owner and any document/folder/subject/semester filters.",
            _ragOptions.ChunkingStrategy,
            _ragOptions.MinChunkChars,
            _ragOptions.MaxSectionChars));
    }

    private ObjectResult ToErrorResult(DocumentException exception) =>
        StatusCode(exception.StatusCode, new ApiErrorResponse
        {
            Code = exception.Code,
            Message = exception.Message,
        });

    private Guid GetSupabaseUserIdFromClaims()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (Guid.TryParse(sub, out var id))
        {
            return id;
        }

        throw new DocumentException(401, "missing_user_id",
            "Authenticated Supabase user id is missing or invalid.");
    }
}
