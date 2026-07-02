using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class OllamaEmbeddingServiceIntegrationTests
{
    [Test]
    [Ignore("Requires running Ollama container")]
    public async Task RealOllama_Returns_384Dim_NonZero_DifferentVectors()
    {
        using var httpClient = new HttpClient();

        var options = OptionsFactory.Create(new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            Model = "all-minilm:l6-v2",
            TimeoutSeconds = 60,
            MaxRetries = 1
        });

        var svc = new OllamaEmbeddingService(
            httpClient,
            options,
            NullLogger<OllamaEmbeddingService>.Instance);

        var v1 = await svc.GenerateEmbeddingAsync("Hello world");
        var v2 = await svc.GenerateEmbeddingAsync("Xin chào thế giới");
        var v3 = await svc.GenerateEmbeddingAsync("Hello world");

        Assert.That(v1, Has.Length.EqualTo(DocumentChunk.EmbeddingDimension));
        Assert.That(v1.Any(x => x != 0), Is.True);
        Assert.That(v1, Is.Not.EqualTo(v2));
        Assert.That(v1, Is.EqualTo(v3).Within(1e-6));
    }
}