using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services.Supabase;

namespace AI_Study_Hub_v2.Services.Rag;

public interface IDocumentStorageReadService
{
    Task<Stream> OpenReadAsync(Document document, CancellationToken cancellationToken = default);
}

public sealed class SupabaseDocumentStorageReadService : IDocumentStorageReadService
{
    private readonly ISupabaseStorageClient _storage;
    private readonly IHttpClientFactory _httpClientFactory;

    public SupabaseDocumentStorageReadService(
        ISupabaseStorageClient storage,
        IHttpClientFactory httpClientFactory)
    {
        _storage = storage;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Stream> OpenReadAsync(Document document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.StoragePath);

        var signedUrl = await _storage.CreateSignedUrlAsync(
            DocumentService.BucketName,
            document.StoragePath,
            DocumentService.SignedUrlTtlSeconds,
            cancellationToken);

        var http = _httpClientFactory.CreateClient(nameof(SupabaseDocumentStorageReadService));
        using var response = await http.GetAsync(signedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var memory = new MemoryStream();
        await response.Content.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }
}
