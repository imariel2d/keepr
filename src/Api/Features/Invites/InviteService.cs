using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Features.Email;
using Keepr.Api.Features.Sharing;
using Keepr.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Invites;

/// <summary>
/// Mints account-invite tokens, sends the claim email, and resolves a presented token. The
/// controllers own the surrounding transaction (create/replace the row, then commit) so account
/// creation and its invite are one unit; this service holds the token, URL, and email logic.
/// See docs/feature-36-account-provisioning.md §8.
/// </summary>
public class InviteService(
    AppDbContext db,
    EmailSenderFactory senders,
    EmailSettingsService settings,
    IOptions<ShareOptions> shareOptions,
    TimeProvider clock)
{
    /// <summary>
    /// Builds a fresh invite for a user and the raw token to email. The invite is returned, not
    /// persisted — the caller adds it in the same transaction as the account change it belongs to.
    /// The raw token exists only here and in the email; only its hash is stored. The expiry now comes
    /// from the admin-managed <c>EmailSettings</c> (§5.1), so this is async.
    /// </summary>
    public async Task<(AccountInvite Invite, string RawToken)> BuildAsync(Guid userId, CancellationToken ct)
    {
        var expiryDays = Math.Max(1, (await settings.GetAsync(ct)).InviteExpiryDays);
        var token = SecureToken.Generate();
        var invite = new AccountInvite
        {
            UserId = userId,
            TokenHash = SecureToken.Hash(token),
            CreatedAt = clock.GetUtcNow(),
            ExpiresAt = clock.GetUtcNow().AddDays(expiryDays)
        };
        return (invite, token);
    }

    /// <summary>Drops any prior invites for a user so a resend supersedes the old link (§8.5).</summary>
    public Task<int> RemoveExistingAsync(Guid userId, CancellationToken ct) =>
        db.AccountInvites.Where(i => i.UserId == userId).ExecuteDeleteAsync(ct);

    /// <summary>Renders and sends the claim email via the currently-configured provider (§5). Throws
    /// on transport failure — callers treat that as non-fatal (the account is already committed, §8.3).
    /// <paramref name="invitedByName"/> is the inviter's display name (first/last), or null to send a
    /// generic "you've been invited" line — the invite must never leak an admin's email address.</summary>
    public async Task SendAsync(string toEmail, string rawToken, string? invitedByName, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var expiryDays = Math.Max(1, s.InviteExpiryDays);
        var claimUrl = $"{ResolveBaseUrl(s.PublicBaseUrl)}/claim/{rawToken}";
        var content = EmailTemplates.Invite(claimUrl, invitedByName, expiryDays);
        var email = await senders.CreateAsync(ct);
        await email.SendAsync(
            new EmailMessage(toEmail, string.Empty, content.Subject, content.HtmlBody, content.TextBody),
            ct);
    }

    /// <summary>Resolves a presented token to its still-claimable invite (with the pending user),
    /// or null if unknown, expired, or already claimed — the caller cannot tell which.</summary>
    public async Task<AccountInvite?> ResolveAsync(string token, CancellationToken ct)
    {
        var hash = SecureToken.Hash(token);
        var invite = await db.AccountInvites
            .Include(i => i.User)
            .SingleOrDefaultAsync(i => i.TokenHash == hash, ct);

        return invite is not null && invite.IsClaimable(clock.GetUtcNow()) ? invite : null;
    }

    /// <summary>The public origin for links in emails: the admin-managed
    /// <c>EmailSettings.PublicBaseUrl</c>, falling back to the share viewer's origin
    /// (<c>Sharing:PublicBaseUrl</c>), which is validated at startup.</summary>
    private string ResolveBaseUrl(string publicBaseUrl)
    {
        var url = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? shareOptions.Value.PublicBaseUrl
            : publicBaseUrl;
        return url.TrimEnd('/');
    }
}
