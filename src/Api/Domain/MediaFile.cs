namespace Media.Api.Domain;

/// <summary>
/// Metadata ledger row for one uploaded object. The bytes live in R2; this is the source of
/// truth for ownership, quota, and lifecycle. See docs/ai-design-decisions.md (D3, D6).
/// </summary>
public class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    /// <summary>Object key in the bucket, e.g. "{ownerId}/{uuid}{ext}". Never the user's filename.</summary>
    public string StorageKey { get; set; } = default!;

    public string OriginalName { get; set; } = default!;

    /// <summary>Sniffed on complete via magic bytes; not the client's declared value.</summary>
    public string? ContentType { get; set; }

    /// <summary>Declared estimate while Pending; actual bytes once Ready.</summary>
    public long SizeBytes { get; set; }

    public string? ChecksumSha256 { get; set; }

    /// <summary>R2 multipart uploadId while in flight; null once complete/aborted.</summary>
    public string? MultipartUploadId { get; set; }

    public MediaStatus Status { get; set; } = MediaStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
