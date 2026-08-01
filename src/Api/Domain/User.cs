namespace Keepr.Api.Domain;

/// <summary>
/// An account. <see cref="UsedBytes"/> is a maintained running total (fast path for the
/// "space remaining" meter) kept in sync with <see cref="MediaFile"/> rows inside the same
/// transaction that changes their status. See docs/ai-design-decisions.md (D3, D9).
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = default!;

    /// <summary>
    /// BCrypt hash of the password. <b>Nullable</b>: an account provisioned by an admin via an email
    /// invite exists (email reserved, role fixed) but has no password until the recipient claims it
    /// and chooses one. A null hash therefore means "cannot sign in yet" — login refuses it. See
    /// docs/feature-36-account-provisioning.md §8.1.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>Authorization level. Defaults to <see cref="Role.User"/>; only the bootstrap
    /// admin or an existing admin's promotion sets <see cref="Role.Admin"/>.</summary>
    public Role Role { get; set; } = Role.User;

    /// <summary>Given name, optional. Populates the avatar initials and the profile screen (#29).</summary>
    public string? FirstName { get; set; }

    /// <summary>Family name, optional (#29).</summary>
    public string? LastName { get; set; }

    /// <summary>
    /// True while the account must change its password before doing anything else. Set when an admin
    /// creates an account with a password they chose (the admin knows it, so the user must rotate it)
    /// and for the bootstrap admin; cleared on the first successful change. An invite-claimed account
    /// never sets it — the user picked that password themselves. See
    /// docs/feature-36-account-provisioning.md §4.1 / §7.3.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Total storage the user is allowed. Default 5 GB.</summary>
    public long QuotaBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>Reserved (pending) + confirmed (ready) bytes currently attributed to the user.</summary>
    public long UsedBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Set when an admin kicks this account. From this moment the account is disabled — login is
    /// refused and its sessions are already revoked — and the background <c>AccountWipeService</c>
    /// hard-deletes every file it owns before removing the row. Null for a normal, live account.
    /// See docs/feature-34-admin-console.md §4.2.
    /// </summary>
    public DateTimeOffset? DeletionRequestedAt { get; set; }

    public ICollection<MediaFile> Files { get; set; } = new List<MediaFile>();
    public ICollection<Session> Sessions { get; set; } = new List<Session>();

    public long RemainingBytes => Math.Max(0, QuotaBytes - UsedBytes);
}
