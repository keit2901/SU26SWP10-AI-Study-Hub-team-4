using System.Net;
using System.Text;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class ChatCompletionClientTests
{
    [Test]
    public async Task GroqClient_BlankRequestedModel_UsesBoundOptionModel()
    {
        var handler = new RecordingHandler("""{"choices":[{"message":{"content":"answer"}}]}""");
        var client = new GroqChatCompletionClient(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new GroqOptions { ApiKey = "test", Model = "configured-groq" }),
            NullLogger<GroqChatCompletionClient>.Instance);

        await client.CompleteAsync(new AiChatCompletionRequest("system", "user"));

        handler.Body.Should().Contain("\"model\":\"configured-groq\"");
    }

    [Test]
    public async Task GeminiClient_BlankRequestedModel_UsesBoundOptionModel()
    {
        var handler = new RecordingHandler("""{"candidates":[{"content":{"parts":[{"text":"answer"}]}}]}""");
        var client = new GeminiChatCompletionClient(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { ApiKey = "test", Model = "configured-gemini" }),
            NullLogger<GeminiChatCompletionClient>.Instance);

        await client.CompleteAsync(new AiChatCompletionRequest("system", "user"));

        handler.RequestUri!.AbsolutePath.Should().Contain("configured-gemini:generateContent");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ProviderClients_TransientHttpFailure_RetriesAtMostOnce(bool useGemini)
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(useGemini
                    ? """{"candidates":[{"content":{"parts":[{"text":"answer"}]}}]}"""
                    : """{"choices":[{"message":{"content":"answer"}}]}""", Encoding.UTF8, "application/json"),
            });
        IAiChatCompletionClient client = useGemini
            ? new GeminiChatCompletionClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new GeminiOptions { ApiKey = "test", Model = "gemini" }), NullLogger<GeminiChatCompletionClient>.Instance)
            : new GroqChatCompletionClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new GroqOptions { ApiKey = "test", Model = "groq" }), NullLogger<GroqChatCompletionClient>.Instance);

        var result = await client.CompleteAsync(new AiChatCompletionRequest("system", "user"));

        result.Should().Be("answer");
        handler.CallCount.Should().Be(2);
    }

    [Test]
    public async Task GroqClient_CallerCancellation_IsNotRetried()
    {
        using var cts = new CancellationTokenSource();
        var handler = new CancelingHandler(cts);
        var client = new GroqChatCompletionClient(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new GroqOptions { ApiKey = "test", Model = "groq" }),
            NullLogger<GroqChatCompletionClient>.Instance);

        var act = () => client.CompleteAsync(new AiChatCompletionRequest("system", "user"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.CallCount.Should().Be(1);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class CancelingHandler(CancellationTokenSource cts) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            cts.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cts.Token);
        }
    }
}
