using System.Security.Claims;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IRecaptchaVerificationService _recaptcha;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IRecaptchaVerificationService recaptcha,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _recaptcha = recaptcha;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var errorMap = new Dictionary<string, string[]>();
            foreach (var (key, entry) in ModelState)
            {
                var messages = entry.Errors?.Select(e => e.ErrorMessage).Where(m => m is not null).Cast<string>().ToArray() ?? [];
                if (messages.Length > 0)
                {
                    errorMap[key] = messages;
                }
            }
            _logger.LogWarning("Register model invalid: {Errors}",
                string.Join("; ", errorMap.SelectMany(kv => kv.Value.Select(v => $"{kv.Key}: {v}"))));
            return BadRequest(new ApiErrorResponse
            {
                Code = "validation_failed",
                Message = "One or more fields are invalid.",
                Errors = errorMap
            });
        }

        var verification = await VerifyRecaptchaAsync(request.RecaptchaToken, "register", cancellationToken);
        if (verification is not null)
        {
            return verification;
        }

        return await ExecuteAsync(() => _authService.RegisterAsync(request, GetUserAgent(), GetIpAddress(), cancellationToken));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var verification = await VerifyRecaptchaAsync(request.RecaptchaToken, "login", cancellationToken);
        if (verification is not null)
        {
            return verification;
        }

        return await ExecuteAsync(() => _authService.LoginAsync(request, GetUserAgent(), GetIpAddress(), cancellationToken));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => _authService.RefreshAsync(request, GetUserAgent(), GetIpAddress(), cancellationToken));
    }

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync()
                ?? throw new AuthException(401, "missing_access_token", "Access token is required for logout.");
            await _authService.LogoutAsync(accessToken, cancellationToken);
            return NoContent();
        }
        catch (AuthException ex)
        {
            return ToErrorResult(ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected logout failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected authentication error occurred."
            });
        }
    }

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() =>
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var email = GetEmailFromClaims();
            return _authService.GetCurrentUserAsync(supabaseUserId, email, cancellationToken);
        });
    }

    [HttpPost("update")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> UpdateUser([FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var supabaseUserId = GetSupabaseUserIdFromClaims();
        var accessToken = await GetAccessTokenAsync()
            ?? throw new AuthException(401, "missing_access_token", "Access token is required for update.");

        return await ExecuteAsync(() => _authService.UpdateUserAsync(
            supabaseUserId,
            accessToken,
            request.Email,
            request.Username,
            request.FullName,
            request.Password,
            cancellationToken
        ));
    }

    private async Task<ActionResult<AuthResponse>?> VerifyRecaptchaAsync(
        string? token,
        string action,
        CancellationToken cancellationToken)
    {
        if (!_recaptcha.ShouldVerify)
        {
            return null;
        }

        var result = await _recaptcha.VerifyAsync(token, GetIpAddress(), action, cancellationToken);
        if (result.Success)
        {
            return null;
        }

        return BadRequest(new ApiErrorResponse
        {
            Code = "recaptcha_failed",
            Message = result.Message,
            Errors = result.ErrorCodes.Count == 0
                ? null
                : new Dictionary<string, string[]> { ["recaptcha"] = result.ErrorCodes.ToArray() }
        });
    }

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (AuthException ex)
        {
            return ToErrorResult(ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected authentication failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected authentication error occurred."
            });
        }
    }

    private ObjectResult ToErrorResult(AuthException exception)
    {
        if (exception.Code is "registration_pending" or "registration_cleanup_pending")
        {
            Response.Headers.RetryAfter = "5";
        }
        var response = new ApiErrorResponse
        {
            Code = exception.Code,
            Message = exception.Message
        };

        return StatusCode(exception.StatusCode, response);
    }

    private Guid GetSupabaseUserIdFromClaims()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (Guid.TryParse(sub, out var id))
        {
            return id;
        }

        throw new AuthException(401, "missing_user_id", "Authenticated Supabase user id is missing or invalid.");
    }

    private string? GetEmailFromClaims()
    {
        // GoTrue puts email both as a top-level "email" claim and inside user_metadata.
        // The JwtBearer handler maps "email" -> ClaimTypes.Email by default, but we also
        // check the raw "email" claim name to be defensive across token shapes.
        var email = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue("email");
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        var token = await HttpContext.GetTokenAsync(JwtBearerDefaults.AuthenticationScheme, "access_token");
        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }
        var auth = Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth.Substring("Bearer ".Length).Trim();
        }
        return null;
    }

    private string? GetUserAgent()
    {
        return Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
    }

    private string? GetIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
