using System.Text.Json;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Payment.Abstractions;
using Microsoft.Extensions.Options;
using PayOS;
using PayOS.Exceptions;
using PayOS.Models.V2.PaymentRequests;
using PayOS.Models.Webhooks;

namespace AI_Study_Hub_v2.Services.Payment;

/// <summary>
/// PayOS implementation of <see cref="IPaymentProvider"/>.
/// Uses the official payOS .NET SDK (v2.x) for payment link creation,
/// webhook verification, and transaction status queries.
/// </summary>
public sealed class PayOsProvider : IPaymentProvider
{
    private readonly PayOSClient _payOs;
    private readonly PayOsSettings _settings;
    private readonly ILogger<PayOsProvider> _logger;

    public string ProviderName => "PayOS";

    public PayOsProvider(IOptions<PayOsSettings> options, ILogger<PayOsProvider> logger)
    {
        _settings = options.Value;
        _payOs = new PayOSClient(_settings.ClientId, _settings.ApiKey, _settings.ChecksumKey);
        _logger = logger;
    }

    public async Task<PaymentLinkResult> CreatePaymentLinkAsync(
        PaymentRequest request, CancellationToken ct = default)
    {
        try
        {
            // PayOS requires a unique orderCode — we derive from TxnRef
            // TxnRef format: "PO_" + orderCode (e.g., "PO_123456789012345")
            var orderCode = ParseOrderCodeFromTxnRef(request.TxnRef);

            var createRequest = new CreatePaymentLinkRequest
            {
                OrderCode = orderCode,
                Amount = request.AmountVnd,
                Description = TruncateDescription(request.Description, 25),
                CancelUrl = request.CancelUrl,
                ReturnUrl = request.ReturnUrl,
                ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(_settings.ExpireMinutes).ToUnixTimeSeconds(),
            };

            var response = await _payOs.PaymentRequests.CreateAsync(createRequest);

            return new PaymentLinkResult(
                Success: true,
                PaymentUrl: response.CheckoutUrl,
                OrderCode: response.OrderCode,
                PaymentLinkId: response.PaymentLinkId,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayOS create payment link failed for TxnRef {TxnRef}", request.TxnRef);
            return new PaymentLinkResult(
                Success: false,
                PaymentUrl: string.Empty,
                OrderCode: 0,
                PaymentLinkId: null,
                ErrorMessage: ex.Message);
        }
    }

    public async Task<WebhookVerificationResult> VerifyWebhookAsync(
        string rawBody, string signature, CancellationToken ct = default)
    {
        try
        {
            // Parse the raw body into a Webhook object for SDK verification
            var webhook = JsonSerializer.Deserialize<Webhook>(rawBody);
            if (webhook is null)
            {
                return new WebhookVerificationResult(false, 0, null, "UNKNOWN", "INVALID", 0, 0, 0, "Invalid webhook body");
            }

            // SDK verifies signature and returns WebhookData (the inner data)
            var verifiedData = await _payOs.Webhooks.VerifyAsync(webhook);

            var providerStatus = verifiedData.Code == "00" ? "PAID" : "UNKNOWN";

            return new WebhookVerificationResult(
                IsValid: true,
                OrderCode: verifiedData.OrderCode,
                PaymentLinkId: verifiedData.PaymentLinkId,
                Status: NormalizeStatus(providerStatus),
                ProviderStatus: providerStatus,
                AmountPaidVnd: verifiedData.Amount,
                ExpectedAmountVnd: verifiedData.Amount,
                AmountRemainingVnd: 0,
                ErrorMessage: null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "PayOS webhook verification failed.");
            return new WebhookVerificationResult(
                false, 0, null, "UNKNOWN", "INVALID", 0, 0, 0,
                $"Signature verification failed: {ex.Message}");
        }
        catch (WebhookException ex)
        {
            _logger.LogWarning(ex, "PayOS webhook signature verification failed.");
            return new WebhookVerificationResult(false, 0, null, "UNKNOWN", "INVALID", 0, 0, 0, "Invalid webhook verification.");
        }
    }

    public async Task<TransactionStatusResult> GetTransactionStatusAsync(
        long orderCode, CancellationToken ct = default)
    {
        try
        {
            if (orderCode <= 0)
            {
                return new TransactionStatusResult(false, orderCode, null, "UNKNOWN", "INVALID", 0, 0, 0);
            }

            var info = await _payOs.PaymentRequests.GetAsync(orderCode);
            var providerStatus = info.Status.ToString();

            return new TransactionStatusResult(
                Success: true,
                OrderCode: info.OrderCode,
                PaymentLinkId: info.Id,
                Status: NormalizeStatus(providerStatus),
                ProviderStatus: providerStatus,
                AmountPaidVnd: info.AmountPaid,
                ExpectedAmountVnd: info.Amount,
                AmountRemainingVnd: info.AmountRemaining);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayOS get transaction status failed for order code {OrderCode}", orderCode);
            return new TransactionStatusResult(false, orderCode, null, "UNKNOWN", "ERROR", 0, 0, 0);
        }
    }

    /// <summary>Generates a unique TxnRef for PayOS with embedded order code.</summary>
    public static string GenerateTxnRef()
    {
        // Use unix timestamp milliseconds as order code (unique enough for MVP)
        var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"PO_{orderCode}";
    }

    /// <summary>Extracts the order code from a TxnRef.</summary>
    public static string NormalizeStatus(string? rawStatus)
    {
        return rawStatus?.Trim().ToUpperInvariant() switch
        {
            "PAID" => "PAID",
            "CANCELLED" => "CANCELLED",
            "EXPIRED" => "EXPIRED",
            "FAILED" => "FAILED",
            "PENDING" => "PENDING",
            "PROCESSING" => "PROCESSING",
            "UNDERPAID" => "UNDERPAID",
            _ => "UNKNOWN"
        };
    }

    public static bool TryParseOrderCode(string? txnRef, out long orderCode)
    {
        orderCode = 0;
        return !string.IsNullOrWhiteSpace(txnRef)
            && txnRef.StartsWith("PO_", StringComparison.Ordinal)
            && long.TryParse(txnRef.AsSpan(3), out orderCode)
            && orderCode is > 0 and <= 9007199254740991;
    }

    private static long ParseOrderCodeFromTxnRef(string txnRef)
    {
        if (TryParseOrderCode(txnRef, out var orderCode)) return orderCode;
        throw new InvalidOperationException("PayOS transaction reference must be PO_<positive digits>.");
    }

    private static string TruncateDescription(string description, int maxLength)
    {
        return description.Length <= maxLength
            ? description
            : description[..maxLength];
    }
}
