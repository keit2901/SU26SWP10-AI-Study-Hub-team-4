using AI_Study_Hub_v2.Services.Payment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AI_Study_Hub_v2.Controllers;

/// <summary>
/// VNPay payment callbacks — IPN (server-to-server) and ReturnUrl (user browser redirect).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class VnPayController : ControllerBase
{
    private readonly IVnPayService _vnPayService;
    private readonly ILogger<VnPayController> _logger;

    public VnPayController(IVnPayService vnPayService, ILogger<VnPayController> logger)
    {
        _vnPayService = vnPayService;
        _logger = logger;
    }

    /// <summary>IPN callback from VNPay server. No auth — VNPay server calls this.</summary>
    [HttpGet("ipn")]
    [AllowAnonymous]
    [EnableRateLimiting("ipn")]
    public async Task<IActionResult> IpnCallback(CancellationToken ct)
    {
        try
        {
            var result = await _vnPayService.ProcessIpnCallbackAsync(Request.Query, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IPN callback processing failed.");
            return Ok(new Dtos.IpnResult("99", "Unknown error"));
        }
    }

    /// <summary>ReturnUrl verification. Auth required — user's browser hits this after VNPay.</summary>
    [HttpGet("return")]
    [Authorize]
    public async Task<IActionResult> ReturnCallback(CancellationToken ct)
    {
        try
        {
            var result = await _vnPayService.VerifyReturnUrlAsync(Request.Query, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Return URL verification failed.");
            return Ok(new Dtos.ReturnUrlResult(false, "error", null, 0, "Verification failed."));
        }
    }
}
