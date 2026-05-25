namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Domain exception emitted by document-related services. Translated to
/// <see cref="Dtos.ApiErrorResponse"/> by <c>DocumentsController</c>.
/// Mirrors <see cref="AuthException"/> so the controller layer pattern is identical.
/// </summary>
public sealed class DocumentException : Exception
{
    public DocumentException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}
