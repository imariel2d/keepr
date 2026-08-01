namespace Keepr.Api.Domain;

/// <summary>
/// The outbound-email provider an admin has configured. <c>Smtp</c> is deliberately absent:
/// SMTP stays an env-only channel (a fallback), never a stored provider — see
/// docs/feature-36-email-providers.md §2.2. Stored as a string (see AppDbContext) so the column
/// reads 'none'/'resend'/… rather than an opaque int.
/// </summary>
public enum EmailProvider
{
    /// <summary>No hosted provider configured — defer to the env SMTP fallback, else send nothing.</summary>
    None = 0,
    Resend = 1,
    Brevo = 2,
    Mailgun = 3
}

/// <summary>
/// App-wide outbound-email configuration, managed at runtime by an admin (#36). A <b>singleton</b>
/// row (<see cref="Id"/> always 1, enforced by a CHECK) — email config is per-app, not per-user.
/// Replaces the boot-time <c>Email__*</c> env binding for the hosted providers; env survives only as
/// the SMTP fallback and the initial seed for <see cref="PublicBaseUrl"/>/<see cref="InviteExpiryDays"/>.
/// See docs/feature-36-email-providers.md §3.
/// </summary>
public class EmailSettings
{
    /// <summary>Fixed singleton key; a CHECK constraint keeps the table single-row.</summary>
    public int Id { get; set; } = 1;

    public EmailProvider Provider { get; set; } = EmailProvider.None;

    /// <summary>Envelope/header From address, e.g. <c>no-reply@keepr.app</c>.</summary>
    public string FromAddress { get; set; } = "";

    /// <summary>Display name on the From address.</summary>
    public string FromName { get; set; } = "Keepr";

    /// <summary>
    /// The provider API key, encrypted at rest with ASP.NET Data Protection (§4). NULL when
    /// <see cref="Provider"/> is <see cref="EmailProvider.None"/> — the invariant the admin API keeps
    /// by clearing it on a switch to none. Never decrypted outside <c>EmailSettingsService</c>, never
    /// returned by the API.
    /// </summary>
    public byte[]? ApiKeyCipher { get; set; }

    /// <summary>Mailgun only: the sending domain (path segment, e.g. <c>mg.keepr.app</c>).</summary>
    public string? MailgunDomain { get; set; }

    /// <summary>Mailgun only: <c>us</c> or <c>eu</c> — selects the fixed regional base URL (§2.1).</summary>
    public string? MailgunRegion { get; set; }

    /// <summary>Public origin for links in emails (the claim link). Seeded from <c>Email:PublicBaseUrl</c>.</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>How long an emailed invite stays claimable. Seeded from <c>Email:InviteExpiryDays</c>.</summary>
    public int InviteExpiryDays { get; set; } = 7;

    /// <summary>When the most recent "send test" ran, and whether it succeeded — surfaced in the UI.</summary>
    public DateTimeOffset? LastTestAt { get; set; }
    public bool? LastTestOk { get; set; }

    /// <summary>The provider's error text from the last failed test, for the admin to diagnose.</summary>
    public string? LastTestError { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The admin who last changed the settings. No FK — a convenience snapshot;
    /// <c>AdminActionLogs</c> is the authoritative audit trail.</summary>
    public Guid? UpdatedByUserId { get; set; }
}
