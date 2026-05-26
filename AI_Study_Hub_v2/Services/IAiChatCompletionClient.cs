namespace AI_Study_Hub_v2.Services;

public interface IAiChatCompletionClient
{
    Task<string> CompleteAsync(
        AiChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AiChatCompletionRequest(
    string SystemPrompt,
    string UserPrompt);

public sealed class AiChatProviderException : Exception
{
    public AiChatProviderException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
