using System.Text.Json;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Payment.Abstractions;
using Microsoft.Extensions.Options;
using PayOS;
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

            // Store the paymentLinkId as the provider transaction ID
            return new PaymentLinkResult(
                Success: true,
                PaymentUrl: response.CheckoutUrl,
                ProviderTxnId: response.PaymentLinkId,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayOS create payment link failed for TxnRef {TxnRef}", request.TxnRef);
            return new PaymentLinkResult(
                Success: false,
                PaymentUrl: string.Empty,
                ProviderTxnId: string.Empty,
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
                return new WebhookVerificationResult(false, string.Empty, "INVALID", 0, "Invalid webhook body");
            }

            // SDK verifies signature and returns WebhookData (the inner data)
            var verifiedData = await _payOs.Webhooks.VerifyAsync(webhook);

            var status = verifiedData.Code == "00"
                ? "PAID"
                : "CANCELLED";

            return new WebhookVerificationResult(
                IsValid: true,
                ProviderTxnId: verifiedData.PaymentLinkId,
                Status: status,
                AmountVnd: verifiedData.Amount,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayOS webhook verification failed.");
            return new WebhookVerificationResult(
                false, string.Empty, "INVALID", 0,
                $"Signature verification failed: {ex.Message}");
        }
    }

    public async Task<TransactionStatusResult> GetTransactionStatusAsync(
        string providerTransactionId, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(providerTransactionId))
            {
                return new TransactionStatusResult(false, "INVALID", 0, false);
            }

            // PayOS SDK uses PaymentLinkId to look up payment info
            var info = await _payOs.PaymentRequests.GetAsync(providerTransactionId);

            var isCompleted = info.Status == PaymentLinkStatus.Paid;

            return new TransactionStatusResult(
                Success: true,
                Status: info.Status.ToString(),
                AmountVnd: info.Amount,
                IsCompleted: isCompleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayOS get transaction status failed for ID {ProviderTxnId}", providerTransactionId);
            return new TransactionStatusResult(false, "ERROR", 0, false);
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
    private static long ParseOrderCodeFromTxnRef(string txnRef)
    {
        if (txnRef.StartsWith("PO_", StringComparison.Ordinal) && long.TryParse(txnRef.AsSpan(3), out var code))
        {
            return code;
        }
        // Fallback: use current timestamp
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static string TruncateDescription(string description, int maxLength)
    {
        return description.Length <= maxLength
            ? description
            : description[..maxLength];
    }
}
