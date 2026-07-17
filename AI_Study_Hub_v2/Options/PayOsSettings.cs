namespace AI_Study_Hub_v2.Options;

/// <summary>
/// Configuration for PayOS payment gateway.
/// Bound from appsettings.json / environment variables (PayOs__*).
/// </summary>
public sealed class PayOsSettings
{
    public const string SectionName = "PayOs";

    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChecksumKey { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = "/api/payment/webhook";
    public int ExpireMinutes { get; set; } = 15;
}
