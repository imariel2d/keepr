namespace Keepr.Api.Domain;

/// <summary>
/// A single-use link that lets its holder set a new password for an existing account. Structurally a
/// twin of <see cref="AccountInvite"/> — same <c>SecureToken</c> construction, same hash-only storage,
/// same one-live-per-account database invariant — but far shorter-lived and marked <see cref="UsedAt"/>
/// rather than claimed.
///
/// Only the token's SHA-256 <see cref="TokenHash"/> is stored; the raw token exists only in the emailed
/// URL, the same rule <see cref="Session"/>, <see cref="ShareLink"/>, and <see cref="AccountInvite"/>
/// follow. A reset is available only to a <see cref="User.EmailVerified"/> account — see
/// docs/feature-26-password-reset.md §3/§4.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The account this resets. Cascades: deleting the user drops the token.</summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    /// <summary>SHA-256 of the reset token. Looked up by equality on a unique index.</summary>
    public byte[] TokenHash { get; set; } = default!;

    /// <summary>Past this the token can no longer be used; a fresh one supersedes it. Short by design
    /// (60 min default, <c>Email:ResetExpiryMinutes</c>) — a reset link is more sensitive than an invite.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the reset completes. A used token is spent and no longer resolves.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>A token is usable while it is neither spent nor past its expiry.</summary>
    public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;
}
