namespace AI_Study_Hub_v2.Services;

public interface IAiChatCompletionClientFactory
{
    IAiChatCompletionClient GetClient(string? modelName);
    string GetProviderName(string? modelName);
}
