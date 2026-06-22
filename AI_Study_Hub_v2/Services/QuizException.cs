namespace AI_Study_Hub_v2.Services;

public sealed class QuizException : Exception
{
    public QuizException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}
