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
    public string PasswordHash { get; set; } = default!;

    /// <summary>Authorization level. Defaults to <see cref="Role.User"/>; only the bootstrap
    /// admin or an existing admin's promotion sets <see cref="Role.Admin"/>.</summary>
    public Role Role { get; set; } = Role.User;

    /// <summary>Total storage the user is allowed. Default 5 GB.</summary>
    public long QuotaBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>Reserved (pending) + confirmed (ready) bytes currently attributed to the user.</summary>
    public long UsedBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Set when an admin kicks this account. From this moment the account is disabled — login is
    /// refused and its sessions are already revoked — and the background <c>AccountWipeService</c>
    /// hard-deletes every file it owns before removing the row. Null for a normal, live account.
    /// See docs/admin-console-design.md §4.2.
    /// </summary>
    public DateTimeOffset? DeletionRequestedAt { get; set; }

    public ICollection<MediaFile> Files { get; set; } = new List<MediaFile>();
    public ICollection<Session> Sessions { get; set; } = new List<Session>();

    public long RemainingBytes => Math.Max(0, QuotaBytes - UsedBytes);
}
