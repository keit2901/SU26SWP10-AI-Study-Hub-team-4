namespace AI_Study_Hub_v2.Services;

public sealed class QuizException : AiStudyFeatureException
{
    public QuizException(int statusCode, string code, string message)
        : base(statusCode, code, message)
    {
    }
}
