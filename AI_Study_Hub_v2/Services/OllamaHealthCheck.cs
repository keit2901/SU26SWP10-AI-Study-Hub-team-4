using System.Net.Http.Json;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Dependency-only diagnostic check. It is deliberately excluded from /health/live
/// so an unavailable embedding service cannot restart an otherwise healthy web app.
/// </summary>
public sealed class OllamaHealthCheck : IHealthCheck
{
    private static readonly TimeSpan MaximumProbeDuration = TimeSpan.FromSeconds(10);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaHealthCheck> _logger;

    public OllamaHealthCheck(IHttpClientFactory httpClientFactory, IOptions<OllamaOptions> options, ILogger<OllamaHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl) || string.IsNullOrWhiteSpace(_options.Model))
        {
            return HealthCheckResult.Unhealthy("Ollama dependency is unavailable.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, Math.Min(_options.TimeoutSeconds, (int)MaximumProbeDuration.TotalSeconds))));

        try
        {
            using var client = _httpClientFactory.CreateClient();
            using var response = await client.GetAsync($"{_options.BaseUrl.TrimEnd('/')}/api/tags", timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy("Ollama dependency is unavailable.");
            }

            var tags = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken: timeout.Token);
            if (tags?.Models?.Any(model => string.Equals(model.Name, _options.Model, StringComparison.Ordinal)) != true)
            {
                return HealthCheckResult.Unhealthy("Ollama dependency is unavailable.");
            }

            return HealthCheckResult.Healthy("Ollama dependency is available.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ollama diagnostic health check failed.");
            return HealthCheckResult.Unhealthy("Ollama dependency is unavailable.");
        }
    }

    private sealed record OllamaTagsResponse(IReadOnlyList<OllamaModelTag>? Models);
    private sealed record OllamaModelTag(string? Name);
}
