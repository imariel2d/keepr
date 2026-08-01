using Keepr.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Email;

/// <summary>
/// One-shot startup seed of the singleton <see cref="Domain.EmailSettings"/> row's
/// <c>PublicBaseUrl</c> / <c>InviteExpiryDays</c> from the legacy <c>Email__*</c> env values, so an
/// existing deployment's config carries over. Runs after migrations. Guarded by
/// <c>UpdatedAt == UnixEpoch</c> (the migration's seed value): once anything — this seed or an admin
/// save — touches the row, env is never read again (§5.1). The provider/key themselves are never
/// seeded from env; hosted providers are configured only through the admin screen.
/// </summary>
public class EmailSettingsSeeder(
    AppDbContext db, IOptions<EmailOptions> env, TimeProvider clock, ILogger<EmailSettingsSeeder> log)
{
    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var row = await db.EmailSettings.FirstOrDefaultAsync(ct);
        // Only seed the untouched, freshly-migrated row.
        if (row is null || row.UpdatedAt != DateTimeOffset.UnixEpoch) return;

        var e = env.Value;
        if (!string.IsNullOrWhiteSpace(e.PublicBaseUrl)) row.PublicBaseUrl = e.PublicBaseUrl;
        if (e.InviteExpiryDays > 0) row.InviteExpiryDays = e.InviteExpiryDays;
        row.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);
        log.LogInformation(
            "Seeded email settings from env (PublicBaseUrl set: {HasUrl}, InviteExpiryDays: {Days}).",
            !string.IsNullOrWhiteSpace(row.PublicBaseUrl), row.InviteExpiryDays);
    }
}
