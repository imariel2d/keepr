namespace Keepr.Api.Domain;

/// <summary>
/// A public "anyone with the link" grant to one file. The unguessable token in the URL *is* the
/// authorization — there is no account behind it. The row stores <see cref="TokenHash"/>, never the
/// token, so a dump of this table cannot be replayed as working links.
///
/// See docs/shareable-links-design.md. This is deliberately not user-to-user sharing (#6).
/// </summary>
public class ShareLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The shared file. Cascade: purging the file removes its links.</summary>
    public Guid MediaFileId { get; set; }
    public MediaFile File { get; set; } = default!;

    /// <summary>Who minted the link. Always the file's owner today; kept for attribution.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>SHA-256 of the URL token. Looked up by equality on a unique index.</summary>
    public byte[] TokenHash { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Required — there is no "never expires". Owner-editable after creation (extend or shorten),
    /// which is how a still-circulating link is kept alive without recreating it. See design §4.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Set when the owner stops sharing. Terminal: a revoked link cannot be re-extended, and
    /// resharing means a new link. A timestamp rather than a flag so "when" survives for an audit.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Last public access. Captured for future analytics/caps (Q-S2); nothing reads it.</summary>
    public DateTimeOffset? LastAccessedAt { get; set; }

    /// <summary>
    /// Whether the link itself still grants access. Independent of the file's own state — a live
    /// link to a trashed file is handled at resolve time, not here.
    /// </summary>
    public bool IsLive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
