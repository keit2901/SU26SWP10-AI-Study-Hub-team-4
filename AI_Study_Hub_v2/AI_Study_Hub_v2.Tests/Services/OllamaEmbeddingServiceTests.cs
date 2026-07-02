using System.Net;
using System.Text;
using System.Text.Json;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Rag;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class OllamaEmbeddingServiceTests
{
    [Test]
    public async Task GenerateEmbeddingAsync_PostsExpectedEndpointAndBody()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(EmbeddingJson(value: 0.25f));

        var sut = CreateSut(handler);

        var result = await sut.GenerateEmbeddingAsync("Xin chào thế giới");

        result.Should().HaveCount(DocumentChunk.EmbeddingDimension);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Url.Should().Be("http://ollama.test/api/embeddings");

        using var json = JsonDocument.Parse(request.Body!);
        json.RootElement.GetProperty("model").GetString().Should().Be("all-minilm:l6-v2");
        json.RootElement.GetProperty("prompt").GetString().Should().Be("Xin chào thế giới");
    }

    [Test]
    public async Task GenerateEmbeddingAsync_Parses384DimResponse()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(EmbeddingJson(value: 0.5f));

        var sut = CreateSut(handler);

        var embedding = await sut.GenerateEmbeddingAsync("semantic search");

        embedding.Should().HaveCount(384);
        embedding.Should().Contain(value => value == 0.5f);
    }

    [Test]
    public async Task GenerateEmbeddingAsync_InvalidDimension_ThrowsAiChatException()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(EmbeddingJson(value: 0.5f, dimensions: 383));

        var sut = CreateSut(handler, maxRetries: 1);

        var act = () => sut.GenerateEmbeddingAsync("bad dimension");

        var ex = await act.Should().ThrowAsync<AiChatException>();
        ex.Which.StatusCode.Should().Be(503);
        ex.Which.Code.Should().Be("embedding_service_unavailable");
    }

    [Test]
    public async Task GenerateEmbeddingAsync_ZeroVector_ThrowsAiChatException()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(EmbeddingJson(value: 0f));

        var sut = CreateSut(handler, maxRetries: 1);

        var act = () => sut.GenerateEmbeddingAsync("zero vector");

        var ex = await act.Should().ThrowAsync<AiChatException>();
        ex.Which.StatusCode.Should().Be(503);
        ex.Which.Code.Should().Be("embedding_service_unavailable");
    }

    [Test]
    public async Task GenerateEmbeddingAsync_RetriesFailuresAndSucceedsOnThirdAttempt()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.InternalServerError);
        handler.EnqueueStatus(HttpStatusCode.BadGateway);
        handler.EnqueueJson(EmbeddingJson(value: 0.75f));

        var sut = CreateSut(handler, maxRetries: 3);

        var embedding = await sut.GenerateEmbeddingAsync("retry test");

        embedding.Should().HaveCount(384);
        handler.Requests.Should().HaveCount(3);
    }

    [Test]
    public async Task GenerateEmbeddingAsync_Timeout_ThrowsAiChatException()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueException(new TaskCanceledException("Simulated timeout."));

        var sut = CreateSut(handler, maxRetries: 1, timeoutSeconds: 1);

        var act = () => sut.GenerateEmbeddingAsync("timeout test");

        var ex = await act.Should().ThrowAsync<AiChatException>();
        ex.Which.StatusCode.Should().Be(503);
        ex.Which.Code.Should().Be("embedding_service_unavailable");
    }

    private static OllamaEmbeddingService CreateSut(
        StubHttpMessageHandler handler,
        int maxRetries = 1,
        int timeoutSeconds = 30)
    {
        var httpClient = new HttpClient(handler);

        var options = OptionsFactory.Create(new OllamaOptions
        {
            BaseUrl = "http://ollama.test",
            Model = "all-minilm:l6-v2",
            TimeoutSeconds = timeoutSeconds,
            MaxRetries = maxRetries
        });

        return new OllamaEmbeddingService(
            httpClient,
            options,
            NullLogger<OllamaEmbeddingService>.Instance);
    }

    private static string EmbeddingJson(float value, int dimensions = DocumentChunk.EmbeddingDimension)
    {
        var embedding = Enumerable.Repeat(value, dimensions).ToArray();
        return JsonSerializer.Serialize(new { embedding });
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();

        public List<CapturedRequest> Requests { get; } = new();

        public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));
        }

        public void EnqueueStatus(HttpStatusCode statusCode)
        {
            _responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage(statusCode)));
        }

        public void EnqueueException(Exception exception)
        {
            _responses.Enqueue(_ => Task.FromException<HttpResponseMessage>(exception));
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                body));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No mocked HTTP response was configured.");
            }

            return await _responses.Dequeue()(cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Url,
        string? Body);
}