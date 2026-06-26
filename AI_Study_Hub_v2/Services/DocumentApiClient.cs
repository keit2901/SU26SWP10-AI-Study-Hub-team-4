using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services.Rag;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Thin wrapper around the DocumentsController endpoints used by Blazor pages.
/// Mirrors <see cref="AuthApiClient"/> in shape: the typed HttpClient is registered
/// in <c>Program.cs</c> with the backend base URL, and request failures bubble up
/// as <see cref="DocumentApiException"/> so pages can render server-provided
/// codes/messages directly.
/// </summary>
public sealed class DocumentApiClient
{
    /// <summary>Server-side cap mirrored from <see cref="DocumentService.MaxFileSizeBytes"/>. Useful for client-side guards.</summary>
    public const long MaxFileSizeBytes = 50L * 1024 * 1024;

    /// <summary>Server-allowed MIME types — kept in sync with <see cref="DocumentService.AllowedMimeTypes"/>.</summary>
    public static readonly IReadOnlySet<string> AllowedMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/msword",
        "application/vnd.ms-powerpoint",
    };

    private readonly HttpClient _http;

    public DocumentApiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Uploads one document via multipart/form-data. Caller owns the stream.</summary>
    public async Task<DocumentDto> UploadAsync(
        string accessToken,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSizeBytes,
        UploadDocumentRequest meta,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(meta);

        using var content = new MultipartFormDataContent();

        var fileContent = new StreamContent(fileStream);
        if (!MediaTypeHeaderValue.TryParse(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, out var mt))
        {
            mt = new MediaTypeHeaderValue("application/octet-stream");
        }
        fileContent.Headers.ContentType = mt;
        if (fileSizeBytes > 0)
        {
            fileContent.Headers.ContentLength = fileSizeBytes;
        }
        content.Add(fileContent, "file", fileName);

        content.Add(new StringContent(meta.SubjectCode ?? string.Empty), "SubjectCode");
        content.Add(new StringContent(meta.Semester ?? string.Empty), "Semester");
        if (meta.FolderId is { } fid)
        {
            content.Add(new StringContent(fid.ToString()), "FolderId");
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/documents/upload")
        {
            Content = content,
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<DocumentDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Lists documents owned by the caller. Pagination is handled client-side for Sprint 1.</summary>
    public async Task<IReadOnlyList<DocumentDto>> ListAsync(
        string accessToken,
        DocumentListQuery? query = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var qs = BuildQueryString(query);
        var path = string.IsNullOrEmpty(qs) ? "api/documents" : $"api/documents?{qs}";

        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var rows = await resp.Content.ReadFromJsonAsync<List<DocumentDto>>(cancellationToken: ct);
            return rows ?? new List<DocumentDto>();
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Returns the extracted text chunks for a document.</summary>
    public async Task<DocumentContentDto> GetContentAsync(
        string accessToken,
        Guid id,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"api/documents/{id}/content");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<DocumentContentDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Fetches one document by id. The returned DTO carries a 5-minute signed download URL.</summary>
    public async Task<DocumentDto> GetAsync(
        string accessToken,
        Guid id,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"api/documents/{id}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<DocumentDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Moves a document into a folder, or back to loose documents.</summary>
    public async Task<DocumentDto> MoveToFolderAsync(
        string accessToken,
        Guid id,
        Guid? folderId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Put, $"api/documents/{id}/folder")
        {
            Content = JsonContent.Create(new MoveDocumentFolderRequest { FolderId = folderId })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<DocumentDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Returns viewer URLs for the Microsoft Office Online Viewer iframe plus a direct download fallback.</summary>
    public async Task<FileViewUrlResponse> GetFileViewUrlAsync(
        string accessToken,
        Guid id,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"api/documents/{id}/file");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            return await resp.Content.ReadFromJsonAsync<FileViewUrlResponse>(cancellationToken: ct)
                ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Renames a document. Only FileName is updated client-side; storage path is unchanged server-side.</summary>
    public async Task<DocumentDto> RenameAsync(
        string accessToken,
        Guid id,
        string newFileName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFileName);

        using var req = new HttpRequestMessage(HttpMethod.Put, $"api/documents/{id}/rename")
        {
            Content = JsonContent.Create(new RenameDocumentRequest { FileName = newFileName })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<DocumentDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Triggers a fresh ingestion pipeline run (re-chunk + re-embed) for the document.</summary>
    public async Task<DocumentIngestionResult> ReIngestAsync(
        string accessToken,
        Guid id,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/documents/{id}/ingest");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            return await resp.Content.ReadFromJsonAsync<DocumentIngestionResult>(cancellationToken: ct)
                ?? new DocumentIngestionResult(id, 0, false, "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Hard-deletes a document. Cascades chunks; storage object removal is best-effort server-side.</summary>
    public async Task DeleteAsync(
        string accessToken,
        Guid id,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Delete, $"api/documents/{id}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            return;
        }
        await ThrowFromResponseAsync(resp, ct);
    }

    private static string BuildQueryString(DocumentListQuery? q)
    {
        if (q is null) return string.Empty;
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(q.SubjectCode)) parts.Add($"SubjectCode={Uri.EscapeDataString(q.SubjectCode)}");
        if (!string.IsNullOrWhiteSpace(q.Semester))    parts.Add($"Semester={Uri.EscapeDataString(q.Semester)}");
        if (q.FolderId is { } fid)                     parts.Add($"FolderId={fid}");
        if (!string.IsNullOrWhiteSpace(q.Q))           parts.Add($"Q={Uri.EscapeDataString(q.Q)}");
        return string.Join('&', parts);
    }

    private static async Task ThrowFromResponseAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var status = (int)resp.StatusCode;
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: ct);
            if (err is not null && (!string.IsNullOrEmpty(err.Code) || !string.IsNullOrEmpty(err.Message)))
            {
                var message = !string.IsNullOrWhiteSpace(err.Message)
                    ? err.Message
                    : $"Request failed with status {status}.";
                throw new DocumentApiException(status, err.Code ?? "request_failed", message, err.Errors);
            }
        }
        catch (DocumentApiException) { throw; }
        catch
        {
            // fall through to generic
        }
        var raw = await resp.Content.ReadAsStringAsync(ct);
        throw new DocumentApiException(status, "request_failed", string.IsNullOrWhiteSpace(raw) ? $"Request failed ({status})." : raw);
    }
}

public sealed class DocumentApiException : Exception
{
    public DocumentApiException(int statusCode, string code, string message, IDictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public IDictionary<string, string[]>? Errors { get; }
}
