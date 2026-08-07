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

    /// <summary>
    /// True once the account holder has <b>proven control of this inbox</b> — by claiming an email
    /// invite, completing an email-based password reset, or being the operator-configured bootstrap
    /// admin. Gates every email-based reset (self-service <i>and</i> admin-emailed link): an
    /// admin-invented, unverified address can never be handed to a stranger via a reset link. Set
    /// only by proving inbox control; an admin typing a password never sets it. See
    /// docs/feature-26-password-reset.md §3 and feature-36-account-provisioning.md §9 (Q-P2).
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// The account's preferred UI language as a supported locale code ("en" | "es" | "fr"), or
    /// <c>null</c> to mean "unset — fall back to the default (English)". Set from the profile screen
    /// (#30). Read on entry to route the user to their locale build, and by the server to localize
    /// outbound email (Phase 3). Never inferred from Accept-Language automatically; a null value is a
    /// real, honored state (default English), not a missing one. Validated against
    /// <see cref="Keepr.Api.Features.Localization.SupportedLanguages"/>. See
    /// docs/feature-30-localization.md §3.1.
    /// </summary>
    public string? PreferredLanguage { get; set; }

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

    /// <summary>
    /// A person's display name from their optional first/last name — "First Last", or whichever part
    /// is set, or <c>null</c> when neither is. Used where we want to name a user (e.g. the inviter line
    /// in the account-invite email) and must never fall back to exposing their email address.
    /// </summary>
    public static string? DisplayName(string? firstName, string? lastName)
    {
        var name = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
