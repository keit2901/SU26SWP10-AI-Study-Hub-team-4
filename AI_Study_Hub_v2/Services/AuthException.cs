namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Domain exception emitted by auth-related services. Translated to <see cref="Dtos.ApiErrorResponse"/>
/// by <see cref="Controllers.AuthController"/>.
/// </summary>
public sealed class AuthException : Exception
{
    public AuthException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}
