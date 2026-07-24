namespace Keepr.Api.Domain;

/// <summary>
/// A logged-in session, addressed by an opaque random token held in an HttpOnly cookie.
///
/// The row stores <see cref="TokenHash"/> — never the token itself — so a dump of this table
/// cannot be replayed as a live session. See docs/cookie-session-design.md (§3.1).
/// </summary>
public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    /// <summary>SHA-256 of the cookie value. Looked up by equality on a unique index.</summary>
    public byte[] TokenHash { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last request this session authenticated. Only written when it drifts by more than a day —
    /// otherwise every read would become a write (§4.1).
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Slides forward with <see cref="LastSeenAt"/>; past this the session is dead.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Set on logout. A timestamp rather than a flag because *when* a session was killed is the
    /// part worth having during an incident, and it costs nothing over a bool.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Captured for a future "active sessions" screen (Q-C3); nothing reads it yet.</summary>
    public string? UserAgent { get; set; }
    public string? CreatedIp { get; set; }

    /// <summary>Live sessions are the ones neither revoked nor past their sliding expiry.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>
    /// Whether this session's expiry is stale enough to be worth writing back.
    ///
    /// Renewing on every request would turn each authenticated read into a write, so the slide is
    /// throttled: with a 30-day window, letting <see cref="LastSeenAt"/> drift by up to a day costs
    /// nothing anyone can observe and caps the writes at one per user per day (§4.1).
    /// </summary>
    public bool NeedsRenewal(DateTimeOffset now, TimeSpan throttle) => now - LastSeenAt >= throttle;
}
