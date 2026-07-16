namespace AI_Study_Hub_v2.Services;

public interface IAiChatCompletionClient
{
    Task<string> CompleteAsync(
        AiChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AiChatCompletionRequest(
    string SystemPrompt,
    string UserPrompt,
    string? ModelName = null,
    int? MaxTokens = null);

public sealed class AiChatProviderException : Exception
{
    public AiChatProviderException(string code, string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int? StatusCode { get; }
}

public sealed class AiChatModelException : Exception
{
    public AiChatModelException(string modelName)
        : base("The requested AI model is not supported.")
    {
        ModelName = modelName;
    }

    public string Code => "unsupported_model";
    public string ModelName { get; }
}
