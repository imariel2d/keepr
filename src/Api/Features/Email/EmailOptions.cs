namespace Keepr.Api.Features.Email;

/// <summary>
/// Outbound-email configuration (env <c>Email__*</c>). Email is <b>optional</b>: with
/// <see cref="Provider"/> unset or <c>none</c> the app registers a no-op sender and every
/// email-dependent feature degrades gracefully (an admin can still create accounts by setting a
/// password directly). See docs/feature-36-account-provisioning.md §6.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary><c>none</c> (default) → no outbound mail. <c>smtp</c> → <see cref="SmtpEmailSender"/>.</summary>
    public string Provider { get; set; } = "none";

    /// <summary>Envelope/header From address, e.g. <c>no-reply@keepr.app</c>.</summary>
    public string FromAddress { get; set; } = "";

    /// <summary>Display name on the From address.</summary>
    public string FromName { get; set; } = "Keepr";

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>
    /// Public origin used to build links in emails (the claim link, later reset). Blank falls back
    /// to <c>Sharing:PublicBaseUrl</c> — the same public SPA origin — resolved by the caller.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>How long an emailed invite stays claimable. Resendable once expired (§8.5).</summary>
    public int InviteExpiryDays { get; set; } = 7;

    /// <summary>How long an emailed password-reset link stays usable, in minutes. Short by design — a
    /// reset link is more sensitive than an invite. A plain config knob (env
    /// <c>Email__ResetExpiryMinutes</c>), not a per-provider setting. See docs/feature-26-password-reset.md §4.</summary>
    public int ResetExpiryMinutes { get; set; } = 60;

    /// <summary>How long an emailed change-email confirmation link stays usable, in minutes. Longer than
    /// a reset link (24 h default) — confirming a new address is less time-critical and the user may not
    /// check the new inbox immediately. A plain config knob (env <c>Email__EmailChangeExpiryMinutes</c>),
    /// not a per-provider setting. See docs/feature-27-change-email.md §5.5.</summary>
    public int EmailChangeExpiryMinutes { get; set; } = 1440;

    /// <summary>
    /// Whether a real sender is configured. Single source of truth for "email is on": Program.cs
    /// uses it to pick the sender, and the admin create path uses it to reject invite mode when no
    /// mail can actually go out (§4.2) rather than silently dropping it.
    /// </summary>
    public bool Enabled =>
        !string.IsNullOrWhiteSpace(Provider) &&
        !Provider.Equals("none", StringComparison.OrdinalIgnoreCase);
}

/// <summary>SMTP transport settings. Any provider that offers SMTP (Gmail, SES, SendGrid, Mailgun,
/// Postmark, Resend, …) works through these — see §6.2 for why SMTP is the generic baseline.</summary>
public class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Upgrade the connection with STARTTLS (the common submission-port 587 flow). When
    /// false, port 465 uses implicit TLS; any other port is treated as an unencrypted dev relay and
    /// may only be used <em>without</em> credentials (see <see cref="SmtpEmailSender"/>).</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>How long to wait on the SMTP host before giving up. Kept short because the send runs
    /// inline on the admin's request — a dead host must not hold the request for MailKit's 2-minute
    /// default. See docs/feature-36-account-provisioning.md §8.3.</summary>
    public int TimeoutSeconds { get; set; } = 15;
}
