using AI_Study_Hub_v2.Services.Payment;
using AI_Study_Hub_v2.Services.Payment.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AI_Study_Hub_v2.Controllers;

/// <summary>
/// PayOS payment webhook endpoint.
/// PayOS sends POST requests here with payment status updates.
/// </summary>
[ApiController]
[Route("api/payment")]
[Produces("application/json")]
public sealed class PayOsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PayOsController> _logger;

    public PayOsController(IPaymentService paymentService, ILogger<PayOsController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>
    /// Webhook receiver for PayOS payment notifications.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [EnableRateLimiting("ipn")]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        try
        {
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync(ct);
            }

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                _logger.LogWarning("PayOS webhook received with empty body.");
                return Ok(new { status = "ignored" });
            }

            var result = await _paymentService.ProcessWebhookAsync(rawBody, ct);

            return result.Disposition == WebhookDisposition.RetryableFailure
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "retry" })
                : Ok(new { status = "processed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayOS webhook processing failed unexpectedly.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { status = "retry" });
        }
    }
}
