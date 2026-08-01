namespace Keepr.Api.Domain;

/// <summary>
/// A pending invitation to claim an admin-provisioned account. The account (<see cref="User"/>) is
/// created up front with a null <see cref="User.PasswordHash"/>; this row carries the emailed claim
/// token that lets the recipient set their own password and activate it.
///
/// Only the token's SHA-256 <see cref="TokenHash"/> is stored — never the raw token, which exists
/// only in the emailed URL — the same rule <see cref="Session"/> and <see cref="ShareLink"/> follow.
/// See docs/feature-36-account-provisioning.md §8.
/// </summary>
public class AccountInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The pending account this invite claims. Cascades: deleting the user drops the invite.</summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    /// <summary>SHA-256 of the claim token. Looked up by equality on a unique index.</summary>
    public byte[] TokenHash { get; set; } = default!;

    /// <summary>Past this the invite can no longer be claimed; a fresh one can be re-sent (§8.5).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the account is claimed. A claimed invite is spent and no longer resolves.</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>An invite is claimable while it is neither spent nor past its expiry.</summary>
    public bool IsClaimable(DateTimeOffset now) => ClaimedAt is null && ExpiresAt > now;
}
