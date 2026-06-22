using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class GroqVisionDescriptionService : IImageDescriptionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string SystemPrompt =
        "You are a precise academic diagram analyzer. " +
        "For each image, produce exactly one paragraph starting with [Image N: and ending with ]. " +
        "Describe in 1-3 sentences what a student needs to understand about this image. " +
        "For diagrams (UML, flowchart, data flow, ERD, circuit, architecture), name the diagram type, " +
        "list the key components, and explain how they connect. " +
        "For charts and graphs, state the axes, trend direction, and notable data points. " +
        "For screenshots or photos, describe the visible content concisely. " +
        "If the image contains text, read it accurately and include it in the description. " +
        "Do not add commentary, opinions, or extra text outside the [Image N: ... ] format.";

    private const int MaxImagesPerRequest = 5;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
    private static readonly int[] RetryDelaysMs = [1_000, 2_000, 4_000];

    private readonly HttpClient _httpClient;
    private readonly GroqOptions _options;
    private readonly ILogger<GroqVisionDescriptionService> _logger;

    public GroqVisionDescriptionService(
        HttpClient httpClient,
        IOptions<GroqOptions> options,
        ILogger<GroqVisionDescriptionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> DescribeAsync(
        IReadOnlyList<ExtractedImage> pageImages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageImages);

        if (pageImages.Count == 0)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Groq API key is not configured — skipping image description.");
            return "[Image: description unavailable (API key not configured)]";
        }

        var descriptions = new List<string>();
        var index = 0;

        for (var batchStart = 0; batchStart < pageImages.Count; batchStart += MaxImagesPerRequest)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = pageImages
                .Skip(batchStart)
                .Take(MaxImagesPerRequest)
                .ToList();

            var description = await DescribeBatchAsync(batch, index + 1, cancellationToken);
            descriptions.Add(description);
            index += batch.Count;
        }

        return string.Join("\n", descriptions);
    }

    private async Task<string> DescribeBatchAsync(
        IReadOnlyList<ExtractedImage> batch,
        int startIndex,
        CancellationToken cancellationToken)
    {
        var contentParts = new List<object> { new { type = "text", text = SystemPrompt } };
        var imageIndex = startIndex;

        var maxImageBytes = _options.MaxImageSizeMb * 1024L * 1024L;

        foreach (var image in batch)
        {
            if (image.Data.Length > maxImageBytes)
            {
                _logger.LogWarning(
                    "Skipping image {Index}: {Size} bytes exceeds {Limit} MB limit.",
                    imageIndex, image.Data.Length, _options.MaxImageSizeMb);
                contentParts.Add(new
                {
                    type = "text",
                    text = $"[Image {imageIndex}: skipped (file too large)]"
                });
                imageIndex++;
                continue;
            }

            var base64 = Convert.ToBase64String(image.Data);
            var mimeType = string.IsNullOrWhiteSpace(image.MimeType)
                ? "image/png"
                : image.MimeType;
            var dataUri = $"data:{mimeType};base64,{base64}";

            contentParts.Add(new
            {
                type = "image_url",
                image_url = new { url = dataUri }
            });

            imageIndex++;
        }

        if (contentParts.Count <= 1)
        {
            return string.Empty;
        }

        var payload = new
        {
            model = _options.VisionModel,
            messages = new[]
            {
                new { role = "user", content = contentParts }
            },
            temperature = 0.1,
            max_tokens = 512,
        };

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        for (var attempt = 0; attempt <= RetryDelaysMs.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var attemptRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri())
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, MediaTypeNames.Application.Json)
                };
                attemptRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                attemptRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

                using var response = await _httpClient.SendAsync(attemptRequest,
                    timeoutCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    var completion = JsonSerializer.Deserialize<GroqChatCompletionResponse>(body, JsonOptions);
                    var answer = completion?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

                    return string.IsNullOrWhiteSpace(answer)
                        ? $"[Image {startIndex}: description unavailable]"
                        : answer;
                }

                if ((int)response.StatusCode == 429 && attempt < RetryDelaysMs.Length)
                {
                    var delay = RetryDelaysMs[attempt];
                    _logger.LogWarning(
                        "Groq vision rate limited (attempt {Attempt}). Retrying in {Delay}ms for batch at index {Start}.",
                        attempt + 1, delay, startIndex);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                if ((int)response.StatusCode >= 500 && attempt == 0)
                {
                    _logger.LogWarning(
                        "Groq vision server error HTTP {StatusCode} (attempt 1). Retrying once for batch at index {Start}.",
                        (int)response.StatusCode, startIndex);
                    await Task.Delay(1_000, cancellationToken);
                    continue;
                }

                _logger.LogWarning(
                    "Groq vision API failed with HTTP {StatusCode} for batch starting at index {Start}.",
                    (int)response.StatusCode, startIndex);
                return $"[Image {startIndex}: description unavailable]";
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Groq vision request timed out after {Timeout}s for batch at index {Start}.",
                    RequestTimeout.TotalSeconds, startIndex);
                return $"[Image {startIndex}: description unavailable (timeout)]";
            }
            catch (Exception ex)
            {
                if (attempt < RetryDelaysMs.Length)
                {
                    _logger.LogWarning(ex,
                        "Groq vision transient failure (attempt {Attempt}). Retrying for batch at index {Start}.",
                        attempt + 1, startIndex);
                    await Task.Delay(RetryDelaysMs[attempt], cancellationToken);
                    continue;
                }

                _logger.LogWarning(ex,
                    "Groq vision API call failed after {Attempts} attempts for batch at index {Start}.",
                    RetryDelaysMs.Length + 1, startIndex);
                return $"[Image {startIndex}: description unavailable]";
            }
        }

        return $"[Image {startIndex}: description unavailable]";
    }

    private Uri BuildChatCompletionsUri()
    {
        var endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
            ? "https://api.groq.com/openai/v1"
            : _options.Endpoint.Trim();

        if (!endpoint.EndsWith('/'))
        {
            endpoint += "/";
        }

        return new Uri(new Uri(endpoint), "chat/completions");
    }

    private sealed record GroqChatCompletionResponse(IReadOnlyList<GroqChoice>? Choices);

    private sealed record GroqChoice(GroqMessage? Message);

    private sealed record GroqMessage(string? Content);
}
