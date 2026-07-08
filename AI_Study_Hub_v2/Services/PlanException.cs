namespace AI_Study_Hub_v2.Services;

public sealed class PlanException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public PlanException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
