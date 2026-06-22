namespace AI_Study_Hub_v2.Services.Supabase;

/// <summary>
/// Minimal Supabase Storage REST client. Phase 2 only needs upload, signed-URL,
/// and delete — we deliberately avoid the full supabase-csharp SDK to keep the
/// dependency surface small and predictable.
///
/// All calls authenticate with the project's service-role key, so they bypass
/// Storage RLS. The bucket is private; clients must hit our backend, never
/// Storage directly.
/// </summary>
public interface ISupabaseStorageClient
{
    /// <summary>
    /// Upload bytes to <c>{bucket}/{objectPath}</c>. Returns the storage path on success.
    /// Set <paramref name="upsert"/>=true to overwrite an existing object at the same path.
    /// </summary>
    Task<string> UploadAsync(
        string bucket,
        string objectPath,
        Stream content,
        string contentType,
        bool upsert = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a short-lived signed URL for downloading <c>{bucket}/{objectPath}</c>.
    /// TTL is in seconds (plan L8 = 300s = 5 min for Phase 2).
    /// </summary>
    Task<string> CreateSignedUrlAsync(
        string bucket,
        string objectPath,
        int ttlSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a file's raw bytes from <c>{bucket}/{objectPath}</c>.
    /// Uses the service-role key so RLS is bypassed. Returns the stream and content type.
    /// </summary>
    Task<(Stream Content, string ContentType)> DownloadFileAsync(
        string bucket,
        string objectPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-delete a single object. Idempotent: missing keys do not throw.
    /// </summary>
    Task DeleteAsync(
        string bucket,
        string objectPath,
        CancellationToken cancellationToken = default);
}
