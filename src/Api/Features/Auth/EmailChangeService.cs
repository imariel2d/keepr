using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Features.Email;
using Keepr.Api.Features.Sharing;
using Keepr.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Auth;

/// <summary>
/// Mints email-change confirmation tokens, sends the confirmation email (to the <b>new</b> address) and
/// the completion heads-up (to the <b>old</b> address), and resolves a presented token. The twin of
/// <see cref="PasswordResetService"/> for the change-email flow: the controllers own the surrounding
/// transaction (create/replace the row, then commit; or resolve/consume); this service holds the token,
/// URL, and email logic. The raw token exists only here and in the email — only its hash is stored. See
/// docs/feature-27-change-email.md §4/§5.
/// </summary>
public class EmailChangeService(
    AppDbContext db,
    EmailSenderFactory senders,
    EmailSettingsService settings,
    IOptions<EmailOptions> emailOptions,
    IOptions<ShareOptions> shareOptions,
    TimeProvider clock)
{
    /// <summary>
    /// Builds a fresh confirmation token for a pending change to <paramref name="newEmail"/>, plus the
    /// raw token to email. The token is returned, not persisted — the caller adds it in the same
    /// transaction as the request. Lifetime comes from <c>Email:EmailChangeExpiryMinutes</c> (§5.5),
    /// range-validated at startup, so it's trusted here with no defensive clamp.
    /// </summary>
    public (EmailChangeToken Token, string RawToken) Build(Guid userId, string newEmail)
    {
        var minutes = emailOptions.Value.EmailChangeExpiryMinutes;
        var raw = SecureToken.Generate();
        var token = new EmailChangeToken
        {
            UserId = userId,
            NewEmail = newEmail,
            TokenHash = SecureToken.Hash(raw),
            CreatedAt = clock.GetUtcNow(),
            ExpiresAt = clock.GetUtcNow().AddMinutes(minutes)
        };
        return (token, raw);
    }

    /// <summary>Drops the user's <b>live</b> (unused) pending change so a new request supersedes the old
    /// link (§5.1). Filtered to <c>UsedAt == null</c> so a confirmed change's spent row survives as
    /// history — matching the one-live partial unique index and <c>MeController.CancelEmailChange</c>,
    /// which "supersede" and "cancel" both mean "drop the live token".</summary>
    public Task<int> RemoveExistingAsync(Guid userId, CancellationToken ct) =>
        db.EmailChangeTokens.Where(t => t.UserId == userId && t.UsedAt == null).ExecuteDeleteAsync(ct);

    /// <summary>Renders and sends the confirmation email to the <b>new</b> address via the currently
    /// configured provider. Throws on transport failure — the caller treats that as non-fatal (the 202
    /// already went out; the profile screen's Resend supersedes and retries).</summary>
    public async Task SendConfirmationAsync(string newEmail, string rawToken, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var minutes = emailOptions.Value.EmailChangeExpiryMinutes; // startup-validated (Program.cs)
        var confirmUrl = $"{ResolveBaseUrl(s.PublicBaseUrl)}/confirm-email/{rawToken}";
        var content = EmailTemplates.ConfirmEmailChange(confirmUrl, minutes);
        var email = await senders.CreateAsync(ct);
        await email.SendAsync(
            new EmailMessage(newEmail, string.Empty, content.Subject, content.HtmlBody, content.TextBody),
            ct);
    }

    /// <summary>Sends the completion heads-up to the <b>old</b> address once a change confirms, so the
    /// original owner can react if it wasn't them (§5.3). The new address is masked so the old inbox
    /// doesn't spell it out. Throws on transport failure — the caller logs it; the change already
    /// committed and must not roll back over a missed notice.</summary>
    public async Task SendChangedNoticeAsync(string oldEmail, string newEmail, CancellationToken ct)
    {
        var content = EmailTemplates.EmailChanged(Mask(newEmail));
        var email = await senders.CreateAsync(ct);
        await email.SendAsync(
            new EmailMessage(oldEmail, string.Empty, content.Subject, content.HtmlBody, content.TextBody),
            ct);
    }

    /// <summary>Resolves a presented token to its still-usable row (with the account), or null if
    /// unknown, expired, or already used — the caller cannot tell which.</summary>
    public async Task<EmailChangeToken?> ResolveAsync(string token, CancellationToken ct)
    {
        var hash = SecureToken.Hash(token);
        var row = await db.EmailChangeTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        return row is not null && row.IsUsable(clock.GetUtcNow()) ? row : null;
    }

    /// <summary>Partially masks an address for the old-inbox heads-up: keeps the first local-part
    /// character and the whole domain, e.g. <c>alex@example.com</c> → <c>a•••@example.com</c>. A single
    /// leading character with nothing else to reveal still masks (e.g. <c>a@x.io</c> → <c>•••@x.io</c>).</summary>
    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "•••";
        var local = email[..at];
        var domain = email[at..]; // includes '@'
        var head = local.Length > 1 ? local[0].ToString() : "";
        return $"{head}•••{domain}";
    }

    /// <summary>The public origin for links in emails: the admin-managed
    /// <c>EmailSettings.PublicBaseUrl</c>, falling back to the share viewer's origin
    /// (<c>Sharing:PublicBaseUrl</c>), which is validated at startup. Mirrors PasswordResetService.</summary>
    private string ResolveBaseUrl(string publicBaseUrl)
    {
        var url = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? shareOptions.Value.PublicBaseUrl
            : publicBaseUrl;
        return url.TrimEnd('/');
    }
}
