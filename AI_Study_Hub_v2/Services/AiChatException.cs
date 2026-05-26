namespace AI_Study_Hub_v2.Services;

public sealed class AiChatException : Exception
{
    public AiChatException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}
