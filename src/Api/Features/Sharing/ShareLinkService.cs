using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Keepr.Api.Data;
using Keepr.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Sharing;

public class ShareOptions
{
    public const string SectionName = "Sharing";

    /// <summary>Ceiling on how far out a link may be set to expire. Requests above it are clamped.</summary>
    public int MaxExpiryDays { get; set; } = 90;

    /// <summary>
    /// Required. The canonical public origin the viewer link is built against, e.g.
    /// <c>https://keepr.app</c> (prod) or <c>http://localhost:4200</c> (dev, the SPA origin).
    /// Validated at startup: an unset or non-absolute-http(s) value fails fast rather than let a
    /// capability URL be built from the untrusted request Host header. See design §6.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>
    /// Global kill-switch. When false, **all** public link resolution is refused without a deploy —
    /// the "take it all down now" lever from the design's Q5 risk acceptance. Owner management still
    /// works so links can be cleaned up. See docs/shareable-links-design.md §2.
    /// </summary>
    public bool PublicAccessEnabled { get; set; } = true;

    /// <summary>Throttle for the best-effort last-accessed stamp, so a read isn't a write each time.</summary>
    public int AccessStampThrottleMinutes { get; set; } = 5;
}

public enum ShareResolveStatus
{
    /// <summary>No such token. Unguessable, so this is genuinely "never existed".</summary>
    NotFound,

    /// <summary>The token existed but the link is revoked or expired, or its file is gone.</summary>
    Gone,

    Ok
}

public readonly record struct ShareResolveResult(ShareResolveStatus Status, ShareLink? Link, MediaFile? File);

public enum UpdateExpiryStatus { NotFound, Revoked, Ok }

/// <summary>
/// Mints, resolves, and manages "anyone with the link" grants. A link's token is the whole
/// authorization; the row stores only its digest. See docs/shareable-links-design.md.
/// </summary>
public class ShareLinkService(AppDbContext db, IOptions<ShareOptions> options, TimeProvider clock)
{
    private readonly ShareOptions _opt = options.Value;

    /// <summary>
    /// Creates a link for a file and returns the raw token — the only time it is available, since
    /// only the digest is stored (design §3.1). The caller must have verified file ownership.
    /// </summary>
    public async Task<(ShareLink Link, string Token)> IssueAsync(
        Guid mediaFileId, Guid ownerId, int expiresInDays, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var token = GenerateToken();

        var link = new ShareLink
        {
            MediaFileId = mediaFileId,
            CreatedByUserId = ownerId,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now.Add(ClampWindow(expiresInDays))
        };
        db.ShareLinks.Add(link);
        await db.SaveChangesAsync(ct);

        return (link, token);
    }

    /// <summary>
    /// Resolves a public token to its file, or explains why it can't. Order matters (design §6):
    /// unknown token → NotFound; revoked/expired → Gone; file trashed or purged → Gone.
    /// Returns Gone (not NotFound) whenever the kill-switch is off, so nothing is disclosed.
    /// </summary>
    public async Task<ShareResolveResult> ResolveAsync(string token, CancellationToken ct)
    {
        if (!_opt.PublicAccessEnabled)
            return new ShareResolveResult(ShareResolveStatus.Gone, null, null);

        var hash = Hash(token);
        var link = await db.ShareLinks.SingleOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (link is null) return new ShareResolveResult(ShareResolveStatus.NotFound, null, null);

        var now = clock.GetUtcNow();
        if (!link.IsLive(now)) return new ShareResolveResult(ShareResolveStatus.Gone, link, null);

        // The global query filter means a trashed or purged file simply isn't found here, which is
        // exactly the "link to a gone file resolves to Gone" rule — no explicit DeletedAt check.
        var file = await db.MediaFiles.SingleOrDefaultAsync(
            m => m.Id == link.MediaFileId && m.Status == MediaStatus.Ready, ct);
        if (file is null) return new ShareResolveResult(ShareResolveStatus.Gone, link, null);

        await StampAccessAsync(link, now, ct);
        return new ShareResolveResult(ShareResolveStatus.Ok, link, file);
    }

    /// <summary>Stops sharing one link. Idempotent; scoped to the owner. Returns false if not theirs.</summary>
    public async Task<bool> RevokeAsync(Guid linkId, Guid ownerId, CancellationToken ct)
    {
        var affected = await db.ShareLinks
            .Where(s => s.Id == linkId && s.CreatedByUserId == ownerId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct);

        if (affected > 0) return true;

        // Distinguish "already revoked / exists" (idempotent success) from "not yours" (404).
        return await db.ShareLinks.AnyAsync(s => s.Id == linkId && s.CreatedByUserId == ownerId, ct);
    }

    /// <summary>Stops sharing a whole file — revokes every live link on it. Returns the count.</summary>
    public async Task<int> RevokeAllForFileAsync(Guid mediaFileId, Guid ownerId, CancellationToken ct) =>
        await db.ShareLinks
            .Where(s => s.MediaFileId == mediaFileId && s.CreatedByUserId == ownerId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct);

    /// <summary>
    /// Changes a link's expiry, measured from now. An expired-but-not-revoked link can be brought
    /// back this way; a revoked link is terminal (design §4).
    /// </summary>
    public async Task<(UpdateExpiryStatus Status, ShareLink? Link)> UpdateExpiryAsync(
        Guid linkId, Guid ownerId, int expiresInDays, CancellationToken ct)
    {
        var link = await db.ShareLinks.SingleOrDefaultAsync(
            s => s.Id == linkId && s.CreatedByUserId == ownerId, ct);
        if (link is null) return (UpdateExpiryStatus.NotFound, null);
        if (link.RevokedAt is not null) return (UpdateExpiryStatus.Revoked, null);

        link.ExpiresAt = clock.GetUtcNow().Add(ClampWindow(expiresInDays));
        await db.SaveChangesAsync(ct);
        return (UpdateExpiryStatus.Ok, link);
    }

    /// <summary>The owner's links for one file, newest first. Never exposes the token.</summary>
    public async Task<IReadOnlyList<ShareLink>> ListForFileAsync(
        Guid mediaFileId, Guid ownerId, CancellationToken ct) =>
        await db.ShareLinks
            .Where(s => s.MediaFileId == mediaFileId && s.CreatedByUserId == ownerId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    /// <summary>
    /// Builds the user-facing viewer URL for a freshly issued token, from the configured public
    /// origin — never from the request host, which is untrusted and wrong behind a proxy or when
    /// the SPA is on a different origin. <see cref="ShareOptions.PublicBaseUrl"/> is validated at
    /// startup, so it is trusted here.
    /// </summary>
    public string BuildUrl(string token) =>
        $"{_opt.PublicBaseUrl.TrimEnd('/')}/s/{token}";

    private TimeSpan ClampWindow(int days) =>
        TimeSpan.FromDays(Math.Clamp(days, 1, _opt.MaxExpiryDays));

    private async Task StampAccessAsync(ShareLink link, DateTimeOffset now, CancellationToken ct)
    {
        // Nothing reads LastAccessedAt yet, so it isn't worth a write on every public GET. Update
        // it only when it has drifted past the throttle — the same instinct as session renewal.
        var throttle = TimeSpan.FromMinutes(_opt.AccessStampThrottleMinutes);
        if (link.LastAccessedAt is { } last && now - last < throttle) return;

        link.LastAccessedAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>256 bits from a CSPRNG: not guessable, so it needs no slow hash on the way in.</summary>
    private static string GenerateToken() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static byte[] Hash(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
