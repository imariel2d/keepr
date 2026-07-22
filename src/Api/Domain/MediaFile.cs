namespace Keepr.Api.Domain;

/// <summary>
/// Metadata ledger row for one uploaded object. The bytes live in R2; this is the source of
/// truth for ownership, quota, and lifecycle. See docs/ai-design-decisions.md (D3, D6).
/// </summary>
public class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    /// <summary>Null = the owner's root. Restrict on delete: folders are drained by app code.</summary>
    public Guid? FolderId { get; set; }
    public Folder? Folder { get; set; }

    /// <summary>Object key in the bucket, e.g. "{ownerId}/{uuid}{ext}". Never the user's filename.</summary>
    public string StorageKey { get; set; } = default!;

    public string OriginalName { get; set; } = default!;

    /// <summary>
    /// Lowercased <see cref="OriginalName"/>. Target of the per-folder uniqueness index (no two
    /// live files in one folder share a name). Always set via <see cref="SetOriginalName"/>.
    /// </summary>
    public string OriginalNameLower { get; set; } = default!;

    /// <summary>Sniffed on complete via magic bytes; not the client's declared value.</summary>
    public string? ContentType { get; set; }

    /// <summary>Declared estimate while Pending; actual bytes once Ready.</summary>
    public long SizeBytes { get; set; }

    public string? ChecksumSha256 { get; set; }

    /// <summary>R2 multipart uploadId while in flight; null once complete/aborted.</summary>
    public string? MultipartUploadId { get; set; }

    public MediaStatus Status { get; set; } = MediaStatus.Pending;

    /// <summary>
    /// Null = live. Set = in trash, and the start of the retention clock. Orthogonal to
    /// <see cref="Status"/>, which tracks the upload lifecycle: a file can be Ready and trashed.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// The item whose deletion put this row in the trash; equals <see cref="Id"/> when the user
    /// trashed this file directly, or the folder's id when it was caught by a subtree cascade.
    /// </summary>
    public Guid? DeletedRootId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Keeps <see cref="OriginalNameLower"/> in lockstep with <see cref="OriginalName"/>.</summary>
    public void SetOriginalName(string name)
    {
        OriginalName = name;
        OriginalNameLower = name.ToLowerInvariant();
    }
}
