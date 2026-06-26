using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/quiz")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class QuizController : ControllerBase
{
    private readonly IQuizService _quizService;
    private readonly ILogger<QuizController> _logger;

    public QuizController(IQuizService quizService, ILogger<QuizController> logger)
    {
        _quizService = quizService;
        _logger = logger;
    }

    [HttpPost("generate")]
    [ProducesResponseType(typeof(QuizDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QuizDto>> Generate(
        [FromBody] GenerateQuizRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ApiErrorResponse { Code = "missing_body", Message = "Request body is required." });
        }

        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var quiz = await _quizService.GenerateAsync(supabaseUserId, request, cancellationToken);
            return Ok(quiz);
        }
        catch (QuizException ex)
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

    [HttpGet("resume")]
    [ProducesResponseType(typeof(QuizDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<QuizDto>> Resume(
        [FromQuery] Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var quiz = await _quizService.ResumeAsync(supabaseUserId, sessionId, cancellationToken);
            return Ok(quiz);
        }
        catch (QuizException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected quiz resume failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while loading the quiz."
            });
        }
    }

    [HttpGet("{quizId:guid}")]
    [ProducesResponseType(typeof(QuizDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<QuizDto>> GetById(
        Guid quizId,
        CancellationToken cancellationToken)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var quiz = await _quizService.GetByIdAsync(supabaseUserId, quizId, cancellationToken);
            return Ok(quiz);
        }
        catch (QuizException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected quiz fetch failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while loading the quiz."
            });
        }
    }

    [HttpPatch("{quizId:guid}/save")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Save(
        Guid quizId,
        [FromBody] SaveQuizRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ApiErrorResponse { Code = "missing_body", Message = "Request body is required." });
        }

        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            await _quizService.SaveAsync(supabaseUserId, quizId, request, cancellationToken);
            return Ok();
        }
        catch (QuizException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected quiz save failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while saving the quiz."
            });
        }
    }

    private ObjectResult ToErrorResult(QuizException exception) =>
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

        throw new QuizException(401, "missing_user_id",
            "Authenticated Supabase user id is missing or invalid.");
    }
}
