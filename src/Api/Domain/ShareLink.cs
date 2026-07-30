namespace Keepr.Api.Domain;

/// <summary>
/// A public "anyone with the link" grant to one file. The unguessable token in the URL *is* the
/// authorization — there is no account behind it.
///
/// The token is stored so the owner can re-copy an active link's URL at any time (design Q-S5).
/// The trade, taken knowingly: a dump of this table exposes the active share URLs — acceptable for
/// a single-owner deployment sharing its own files, and revisited before multi-user sharing (#6).
///
/// See docs/feature-7-shareable-links.md. This is deliberately not user-to-user sharing (#6).
/// </summary>
public class ShareLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The shared file. Cascade: purging the file removes its links.</summary>
    public Guid MediaFileId { get; set; }
    public MediaFile File { get; set; } = default!;

    /// <summary>Who minted the link. Always the file's owner today; kept for attribution.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>The URL token — the capability itself. Looked up by equality on a unique index.</summary>
    public string Token { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the link stops granting access, or <c>null</c> for a link that never expires.
    /// Owner-editable after creation (extend, shorten, or switch to/from never), which is how a
    /// still-circulating link is kept alive without recreating it. See design §4.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Set when the owner stops sharing. Terminal: a revoked link cannot be re-extended, and
    /// resharing means a new link. A timestamp rather than a flag so "when" survives for an audit.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Last public access. Captured for future analytics/caps (Q-S2); nothing reads it.</summary>
    public DateTimeOffset? LastAccessedAt { get; set; }

    /// <summary>
    /// Whether the link itself still grants access. Independent of the file's own state — a live
    /// link to a trashed file is handled at resolve time, not here. A null expiry never lapses.
    /// </summary>
    public bool IsLive(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
