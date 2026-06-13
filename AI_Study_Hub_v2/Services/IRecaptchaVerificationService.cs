using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Study_Hub_v2.Services;

public interface IRecaptchaVerificationService
{
    bool IsEnabled { get; }

    bool IsConfigured { get; }

    Task<RecaptchaVerificationResult> VerifyAsync(
        string? token,
        string? remoteIp = null,
        string? expectedAction = null,
        CancellationToken cancellationToken = default);
}

public sealed record RecaptchaVerificationResult(
    bool Success,
    string Message,
    IReadOnlyList<string> ErrorCodes)
{
    public static RecaptchaVerificationResult Valid(string message = "Verification passed.")
        => new(true, message, System.Array.Empty<string>());

    public static RecaptchaVerificationResult Invalid(string message, IReadOnlyList<string>? errorCodes = null)
        => new(false, message, errorCodes ?? System.Array.Empty<string>());
}
