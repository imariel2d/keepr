namespace Media.Api.Storage;

/// <summary>
/// Storage abstraction over the S3 API. Backed by Cloudflare R2 in prod and MinIO locally,
/// so the same code path runs in both. See docs/ai-design-decisions.md (D1, D2, D7).
/// </summary>
public interface IObjectStorage
{
    /// <summary>Begin a multipart upload; returns the provider uploadId.</summary>
    Task<string> CreateMultipartUploadAsync(string key, string? contentType, CancellationToken ct = default);

    /// <summary>Presigned URL the browser uses to PUT one part directly to storage.</summary>
    Task<string> PresignUploadPartUrlAsync(string key, string uploadId, int partNumber, CancellationToken ct = default);

    /// <summary>Assemble the parts into the final object. Returns the object's size in bytes.</summary>
    Task<long> CompleteMultipartUploadAsync(
        string key, string uploadId, IReadOnlyList<CompletedPart> parts, CancellationToken ct = default);

    /// <summary>Release the parts of an abandoned/failed upload.</summary>
    Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken ct = default);

    /// <summary>Presigned URL for the owner to download/view the object.</summary>
    Task<string> PresignGetUrlAsync(string key, CancellationToken ct = default);

    /// <summary>Read the first <paramref name="count"/> bytes of an object (for content sniffing).</summary>
    Task<byte[]> ReadHeadBytesAsync(string key, int count, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}

public readonly record struct CompletedPart(int PartNumber, string ETag);
