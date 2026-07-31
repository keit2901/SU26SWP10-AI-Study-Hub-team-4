using System.Net;
using System.Text;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class OllamaHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_ExactConfiguredModelPresent_IsHealthy()
    {
        var sut = CreateSut(HttpStatusCode.OK, "{\"models\":[{\"name\":\"all-minilm:l6-v2\"}]}");

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task CheckHealthAsync_ModelMissing_IsUnhealthy()
    {
        var sut = CreateSut(HttpStatusCode.OK, "{\"models\":[{\"name\":\"other-model\"}]}");

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Ollama dependency is unavailable.");
    }

    [Test]
    public async Task CheckHealthAsync_OllamaUnavailable_IsUnhealthyWithoutThrowing()
    {
        var sut = CreateSut(HttpStatusCode.ServiceUnavailable, "");

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Ollama dependency is unavailable.");
    }

    private static OllamaHealthCheck CreateSut(HttpStatusCode statusCode, string body) =>
        new(
            new StubHttpClientFactory(statusCode, body),
            OptionsFactory.Create(new OllamaOptions
            {
                BaseUrl = "http://ollama.test",
                Model = "all-minilm:l6-v2",
                TimeoutSeconds = 60,
            }),
            NullLogger<OllamaHealthCheck>.Instance);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHttpClientFactory(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public HttpClient CreateClient(string name) => new(new StubHandler(_statusCode, _body));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }
}
