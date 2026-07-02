namespace AI_Study_Hub_v2.Services;

public class AiStudyFeatureException : Exception
{
    public AiStudyFeatureException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}
