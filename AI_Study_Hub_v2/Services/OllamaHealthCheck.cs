using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class OllamaHealthCheck : IHostedService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaHealthCheck> _logger;

    public OllamaHealthCheck(
        IHttpClientFactory httpClientFactory,
        IOptions<OllamaOptions> options,
        ILogger<OllamaHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _logger.LogWarning("Ollama health check skipped because Ollama:BaseUrl is not configured.");
            return;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));

            var baseUrl = _options.BaseUrl.TrimEnd('/');
            using var response = await httpClient.GetAsync($"{baseUrl}/api/tags", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Ollama health check passed. BaseUrl={BaseUrl}, Model={Model}",
                    _options.BaseUrl,
                    _options.Model);
                return;
            }

            _logger.LogWarning(
                "Ollama health check failed. BaseUrl={BaseUrl}, StatusCode={StatusCode}. Embedding may be unavailable.",
                _options.BaseUrl,
                response.StatusCode);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Ollama not available at startup. BaseUrl={BaseUrl}. Embedding may be unavailable.",
                _options.BaseUrl);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}