using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/ai/chat")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AiChatController : ControllerBase
{
    private readonly IAiChatService _service;
    private readonly ILogger<AiChatController> _logger;

    public AiChatController(IAiChatService service, ILogger<AiChatController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("ask")]
    [ProducesResponseType(typeof(AiChatAnswerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AiChatAnswerResponse>> Ask(
        [FromBody] AiChatAskRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "missing_body",
                Message = "Request body is required."
            });
        }

        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var response = await _service.AskAsync(supabaseUserId, request, cancellationToken);
            return Ok(response);
        }
        catch (AiChatException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected AI chat failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while answering the question."
            });
        }
    }

    private ObjectResult ToErrorResult(AiChatException exception) =>
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

        throw new AiChatException(401, "missing_user_id",
            "Authenticated Supabase user id is missing or invalid.");
    }
}
