using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Features.Email;
using Keepr.Api.Features.Sharing;
using Keepr.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Auth;

/// <summary>
/// Mints password-reset tokens, sends the reset email, and resolves a presented token. The twin of
/// <see cref="Invites.InviteService"/> for the reset flow: the controllers own the surrounding
/// transaction (create/replace the row, then commit); this service holds the token, URL, and email
/// logic. The raw token exists only here and in the email — only its hash is stored. See
/// docs/feature-26-password-reset.md §4/§5.
/// </summary>
public class PasswordResetService(
    AppDbContext db,
    EmailSenderFactory senders,
    EmailSettingsService settings,
    IOptions<EmailOptions> emailOptions,
    IOptions<ShareOptions> shareOptions,
    TimeProvider clock)
{
    /// <summary>
    /// Builds a fresh reset token for a user and the raw token to email. The token is returned, not
    /// persisted — the caller adds it in the same transaction as the request it belongs to. Lifetime
    /// comes from <c>Email:ResetExpiryMinutes</c> (§4), clamped to at least one minute.
    /// </summary>
    public (PasswordResetToken Token, string RawToken) Build(Guid userId)
    {
        // Range-validated at startup (Program.cs), so trust it here — no defensive clamp.
        var minutes = emailOptions.Value.ResetExpiryMinutes;
        var raw = SecureToken.Generate();
        var token = new PasswordResetToken
        {
            UserId = userId,
            TokenHash = SecureToken.Hash(raw),
            CreatedAt = clock.GetUtcNow(),
            ExpiresAt = clock.GetUtcNow().AddMinutes(minutes)
        };
        return (token, raw);
    }

    /// <summary>Drops any prior reset tokens for a user so a new request supersedes the old link (§5.1).</summary>
    public Task<int> RemoveExistingAsync(Guid userId, CancellationToken ct) =>
        db.PasswordResetTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);

    /// <summary>Renders and sends the reset email via the currently-configured provider. Throws on
    /// transport failure — self-service callers treat that as non-fatal (the neutral 202 already went
    /// out); the admin-initiated path surfaces it (§5.1/§6.2).</summary>
    public async Task SendAsync(string toEmail, string rawToken, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var minutes = emailOptions.Value.ResetExpiryMinutes; // startup-validated (Program.cs)
        var resetUrl = $"{ResolveBaseUrl(s.PublicBaseUrl)}/reset-password/{rawToken}";
        var content = EmailTemplates.PasswordReset(resetUrl, minutes);
        var email = await senders.CreateAsync(ct);
        await email.SendAsync(
            new EmailMessage(toEmail, string.Empty, content.Subject, content.HtmlBody, content.TextBody),
            ct);
    }

    /// <summary>Resolves a presented token to its still-usable row (with the account), or null if
    /// unknown, expired, or already used — the caller cannot tell which.</summary>
    public async Task<PasswordResetToken?> ResolveAsync(string token, CancellationToken ct)
    {
        var hash = SecureToken.Hash(token);
        var row = await db.PasswordResetTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        return row is not null && row.IsUsable(clock.GetUtcNow()) ? row : null;
    }

    /// <summary>The public origin for links in emails: the admin-managed
    /// <c>EmailSettings.PublicBaseUrl</c>, falling back to the share viewer's origin
    /// (<c>Sharing:PublicBaseUrl</c>), which is validated at startup. Mirrors InviteService.</summary>
    private string ResolveBaseUrl(string publicBaseUrl)
    {
        var url = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? shareOptions.Value.PublicBaseUrl
            : publicBaseUrl;
        return url.TrimEnd('/');
    }
}
