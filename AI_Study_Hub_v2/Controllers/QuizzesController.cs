using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/ai/quizzes")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class QuizzesController : ControllerBase
{
    private readonly IQuizService _service;
    private readonly ILogger<QuizzesController> _logger;

    public QuizzesController(IQuizService service, ILogger<QuizzesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("generate")]
    [ProducesResponseType(typeof(QuizGenerateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizGenerateResponse>> Generate(
        [FromBody] QuizGenerateRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ApiErrorResponse { Code = "missing_body", Message = "Request body is required." });
        }

        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var response = await _service.GenerateAsync(supabaseUserId, request, cancellationToken);
            return Ok(response);
        }
        catch (AiStudyFeatureException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected quiz generation failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while generating the quiz."
            });
        }
    }

    [HttpPost("{quizId:guid}/submit")]
    [ProducesResponseType(typeof(QuizSubmitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizSubmitResponse>> Submit(
        Guid quizId,
        [FromBody] QuizSubmitRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ApiErrorResponse { Code = "missing_body", Message = "Request body is required." });
        }

        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var response = await _service.SubmitAsync(supabaseUserId, quizId, request, cancellationToken);
            return Ok(response);
        }
        catch (AiStudyFeatureException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected quiz submission failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while submitting the quiz."
            });
        }
    }

    private ObjectResult ToErrorResult(AiStudyFeatureException exception) =>
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

        throw new AiStudyFeatureException(401, "missing_user_id",
            "Authenticated Supabase user id is missing or invalid.");
    }
}
