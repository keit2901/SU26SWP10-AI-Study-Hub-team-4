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
    private readonly IChatPersistenceService _persistence;
    private readonly ILogger<AiChatController> _logger;

    public AiChatController(IAiChatService service, IChatPersistenceService persistence, ILogger<AiChatController> logger)
    {
        _service = service;
        _persistence = persistence;
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

            // Ensure a session exists — use the provided one or create a new one
            var sessionId = request.SessionId;
            if (sessionId is null)
            {
                var newSession = await _persistence.CreateSessionAsync(supabaseUserId, new CreateChatSessionRequest
                {
                    FolderId = request.FolderId,
                    Model = request.Model,
                    TopK = request.TopK,
                }, cancellationToken);
                sessionId = newSession.Id;
            }

            // Load recent chat history so the AI has conversation context
            var chatHistory = await _persistence.GetMessagesAsync(supabaseUserId, sessionId.Value, cancellationToken);
            request = request with { ChatHistory = chatHistory };

            var response = await _service.AskAsync(supabaseUserId, request, cancellationToken);

            // Persist the exchange
            var scopeLabel = BuildScopeLabel(request);
            await _persistence.SaveExchangeAsync(supabaseUserId, sessionId.Value, request.Question, scopeLabel, response, cancellationToken);

            return Ok(response with { SessionId = sessionId.Value });
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

    [HttpGet("sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatSessionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatSessionDto>>> ListSessions(
        [FromQuery] Guid? folderId,
        CancellationToken cancellationToken)
    {
        var supabaseUserId = GetSupabaseUserIdFromClaims();
        var sessions = await _persistence.ListSessionsAsync(supabaseUserId, folderId, cancellationToken);
        return Ok(sessions);
    }

    [HttpPost("sessions")]
    [ProducesResponseType(typeof(ChatSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChatSessionDto>> CreateSession(
        [FromBody] CreateChatSessionRequest request,
        CancellationToken cancellationToken)
    {
        var supabaseUserId = GetSupabaseUserIdFromClaims();
        var session = await _persistence.CreateSessionAsync(supabaseUserId, request, cancellationToken);
        return Ok(session);
    }

    [HttpGet("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetSessionMessages(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var supabaseUserId = GetSupabaseUserIdFromClaims();
        var messages = await _persistence.GetMessagesAsync(supabaseUserId, sessionId, cancellationToken);
        return Ok(messages);
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken cancellationToken)
    {
        var supabaseUserId = GetSupabaseUserIdFromClaims();
        await _persistence.DeleteSessionAsync(supabaseUserId, sessionId, cancellationToken);
        return NoContent();
    }

    private static string BuildScopeLabel(AiChatAskRequest request)
    {
        var hasSelection = request.DocumentIds is { Count: > 0 } || request.DocumentId is not null;
        if (!hasSelection)
        {
            return "General knowledge (no files selected)";
        }

        var scope = request.FolderId is not null ? "Selected folder" : "All indexed documents";
        var count = request.DocumentIds?.Count ?? 1;
        return $"{scope} | {count} selected file{(count == 1 ? string.Empty : "s")}";
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
