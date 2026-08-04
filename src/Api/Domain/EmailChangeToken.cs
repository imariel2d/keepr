namespace Keepr.Api.Domain;

/// <summary>
/// A single-use link that confirms a pending change of an account's login email. Structurally a twin
/// of <see cref="PasswordResetToken"/> — same <c>SecureToken</c> construction, same hash-only storage,
/// same one-live-per-account database invariant — with one extra column: the <see cref="NewEmail"/>
/// the account moves to when the link is confirmed. The row <b>is</b> the pending state; there is no
/// separate flag on <see cref="User"/>.
///
/// Only the token's SHA-256 <see cref="TokenHash"/> is stored; the raw token exists only in the emailed
/// URL, the same rule <see cref="Session"/>, <see cref="ShareLink"/>, <see cref="AccountInvite"/>, and
/// <see cref="PasswordResetToken"/> follow. Confirming the link proves control of the new inbox and is
/// the only self-service path that sets <see cref="User.EmailVerified"/> for the new address. See
/// docs/feature-27-change-email.md §4/§5.
/// </summary>
public class EmailChangeToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The account being changed. Cascades: deleting the user drops the token.</summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    /// <summary>The normalized (trimmed, lowercased) address the account moves to on confirm. Validated
    /// against <c>EmailPolicy</c> and checked for uniqueness at request time and again at confirm.</summary>
    public string NewEmail { get; set; } = default!;

    /// <summary>SHA-256 of the confirmation token. Looked up by equality on a unique index.</summary>
    public byte[] TokenHash { get; set; } = default!;

    /// <summary>Past this the token can no longer be used; a fresh request supersedes it. Longer than a
    /// reset link (24 h default, <c>Email:EmailChangeExpiryMinutes</c>) — confirming a new inbox is less
    /// time-critical and the user may not check the new address immediately.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the change is confirmed. A used token is spent and no longer resolves.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>A token is usable while it is neither spent nor past its expiry.</summary>
    public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;
}
