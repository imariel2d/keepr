using System.Text;
using Keepr.Api.Data;
using Keepr.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Email;

/// <summary>
/// A resolved snapshot of the email settings, with the API key <b>decrypted</b>. Produced only by
/// <see cref="EmailSettingsService"/> and handed to a transport for one send; never serialized.
/// </summary>
public sealed record ResolvedEmailSettings(
    EmailProvider Provider, string FromAddress, string FromName, string? ApiKey,
    string? MailgunDomain, string? MailgunRegion, string PublicBaseUrl, int InviteExpiryDays);

/// <summary>
/// Reads the singleton <see cref="EmailSettings"/> row and decrypts its API key on demand, and owns
/// the Data-Protection protector that seals/opens the key. Sends are rare (only an invite or a test),
/// so the row is read per call — no cache to invalidate. Replaces the static
/// <c>EmailOptions.Enabled</c> as the single source of truth for "email is on". See
/// docs/feature-36-email-providers.md §4/§5.
/// </summary>
public class EmailSettingsService(
    AppDbContext db, IDataProtectionProvider dataProtection, IOptions<EmailOptions> envEmail)
{
    /// <summary>Purpose string scoping the protector — changing it would orphan every stored key.</summary>
    public const string ProtectorPurpose = "Keepr.EmailSettings.ApiKey";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    /// <summary>The singleton row (Id = 1). Seeded by the migration, so it always exists. If the seed
    /// is somehow missing, a fresh default is <b>added and tracked</b> (not just returned), so a caller
    /// that mutates it and saves actually persists it instead of silently writing zero rows.</summary>
    public async Task<EmailSettings> GetRowAsync(CancellationToken ct)
    {
        var row = await db.EmailSettings.FirstOrDefaultAsync(ct);
        if (row is not null) return row;

        row = new EmailSettings { Id = 1 };
        db.EmailSettings.Add(row);
        return row;
    }

    /// <summary>The current settings with the API key decrypted, ready to hand to a transport.</summary>
    public async Task<ResolvedEmailSettings> GetAsync(CancellationToken ct)
    {
        var row = await GetRowAsync(ct);
        var key = row.ApiKeyCipher is { Length: > 0 } cipher ? Decrypt(cipher) : null;
        return new ResolvedEmailSettings(
            row.Provider, row.FromAddress, row.FromName, key,
            row.MailgunDomain, row.MailgunRegion, row.PublicBaseUrl, row.InviteExpiryDays);
    }

    /// <summary>
    /// Whether outbound mail can actually go out: a hosted provider is configured in the DB, or the
    /// env SMTP fallback is set (§5.1). The admin create path gates invite mode on this.
    /// </summary>
    public async Task<bool> IsEnabledAsync(CancellationToken ct) =>
        (await GetRowAsync(ct)).Provider != EmailProvider.None || EnvSmtpEnabled;

    /// <summary>True when the legacy env channel (<c>Email__Provider=smtp</c>) is configured — the
    /// fallback the factory uses when the DB provider is <see cref="EmailProvider.None"/>.</summary>
    public bool EnvSmtpEnabled =>
        envEmail.Value.Enabled &&
        envEmail.Value.Provider.Equals("smtp", StringComparison.OrdinalIgnoreCase);

    /// <summary>Seals a plaintext API key for storage. The ciphertext is only ever unsealed here.</summary>
    public byte[] Encrypt(string plaintext) => _protector.Protect(Encoding.UTF8.GetBytes(plaintext));

    private string Decrypt(byte[] cipher) => Encoding.UTF8.GetString(_protector.Unprotect(cipher));
}
