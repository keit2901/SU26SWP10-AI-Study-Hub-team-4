using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Supabase;

/// <summary>
/// HttpClient-backed implementation of <see cref="ISupabaseStorageClient"/>.
/// HttpClient is configured in Program.cs with the Storage base URL + the
/// service-role bearer token. We never expose this client to Blazor — it lives
/// strictly inside backend services.
/// </summary>
public sealed class SupabaseStorageClient : ISupabaseStorageClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly SupabaseOptions _options;
    private readonly ILogger<SupabaseStorageClient> _logger;

    public SupabaseStorageClient(
        HttpClient http,
        IOptions<SupabaseOptions> options,
        ILogger<SupabaseStorageClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        string bucket,
        string objectPath,
        Stream content,
        string contentType,
        bool upsert = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
        ArgumentNullException.ThrowIfNull(content);

        // Storage REST: POST /object/{bucket}/{path}
        // Header `x-upsert: true` = overwrite if exists.
        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"object/{bucket}/{objectPath}")
        {
            Content = streamContent,
        };
        if (upsert)
        {
            req.Headers.TryAddWithoutValidation("x-upsert", "true");
        }

        using var resp = await _http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Supabase Storage upload failed: bucket={Bucket} path={Path} status={Status} body={Body}",
                bucket, objectPath, resp.StatusCode, body);
            throw new SupabaseStorageException(
                $"Upload to {bucket}/{objectPath} failed with HTTP {(int)resp.StatusCode}: {body}");
        }

        return objectPath;
    }

    public async Task<string> CreateSignedUrlAsync(
        string bucket,
        string objectPath,
        int ttlSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
        if (ttlSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ttlSeconds), "TTL must be positive.");
        }

        // Storage REST: POST /object/sign/{bucket}/{path}  body { expiresIn: <seconds> }
        var payload = new { expiresIn = ttlSeconds };
        using var resp = await _http.PostAsJsonAsync(
            $"object/sign/{bucket}/{objectPath}", payload, JsonOpts, cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Supabase Storage signed URL creation failed: bucket={Bucket} path={Path} status={Status} body={Body}",
                bucket, objectPath, resp.StatusCode, body);
            throw new SupabaseStorageException(
                $"Sign URL for {bucket}/{objectPath} failed with HTTP {(int)resp.StatusCode}: {body}");
        }

        var signed = await resp.Content.ReadFromJsonAsync<SignedUrlResponse>(JsonOpts, cancellationToken)
            ?? throw new SupabaseStorageException("Sign URL response was empty.");

        if (string.IsNullOrWhiteSpace(signed.SignedUrl))
        {
            throw new SupabaseStorageException("Sign URL response missing 'signedURL' field.");
        }

        // Storage returns a relative URL like "/object/sign/...". Compose it onto the
        // Supabase public URL so callers can hit it directly through Kong.
        var publicBase = _options.Url.TrimEnd('/');
        var relative = signed.SignedUrl.StartsWith("/", StringComparison.Ordinal)
            ? signed.SignedUrl
            : "/" + signed.SignedUrl;
        return $"{publicBase}/storage/v1{relative}";
    }

    public async Task<(Stream Content, string ContentType)> DownloadFileAsync(
        string bucket,
        string objectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);

        // Storage REST: GET /object/{bucket}/{path} returns the raw file content.
        // Build the request manually with explicit auth to handle quirks of local Supabase.
        var relativePath = $"object/{bucket}/{objectPath}";
        using var req = new HttpRequestMessage(HttpMethod.Get, relativePath);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        req.Headers.TryAddWithoutValidation("apikey", _options.ServiceRoleKey);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Supabase Storage download failed: bucket={Bucket} path={Path} status={Status} body={Body}",
                bucket, objectPath, resp.StatusCode, body);
            throw new SupabaseStorageException(
                $"Download {bucket}/{objectPath} failed with HTTP {(int)resp.StatusCode}: {body}");
        }

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var ms = new System.IO.MemoryStream();
        await resp.Content.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;
        return (ms, contentType);
    }

    public async Task DeleteAsync(string bucket, string objectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);

        using var req = new HttpRequestMessage(HttpMethod.Delete, $"object/{bucket}/{objectPath}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        req.Headers.TryAddWithoutValidation("apikey", _options.ServiceRoleKey);

        using var resp = await _http.SendAsync(req, cancellationToken);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Supabase Storage delete failed: bucket={Bucket} path={Path} status={Status} body={Body}",
                bucket, objectPath, resp.StatusCode, body);
            throw new SupabaseStorageException(
                $"Delete {bucket}/{objectPath} failed with HTTP {(int)resp.StatusCode}: {body}");
        }
    }

    private sealed class SignedUrlResponse
    {
        [JsonPropertyName("signedURL")]
        public string? SignedUrl { get; set; }
    }
}

public sealed class SupabaseStorageException : Exception
{
    public SupabaseStorageException(string message) : base(message) { }

    public SupabaseStorageException(string message, Exception inner) : base(message, inner) { }
}
