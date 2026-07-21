namespace AI_Study_Hub_v2.Services.Payment;

/// <summary>
/// Raised when the external payment gateway cannot create or verify a payment link.
/// Keeps provider details for logs while exposing a safe user-facing message.
/// </summary>
public sealed class PaymentProviderException : Exception
{
    public PaymentProviderException(
        string message,
        string? providerMessage = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderMessage = providerMessage;
    }

    public string? ProviderMessage { get; }
}
