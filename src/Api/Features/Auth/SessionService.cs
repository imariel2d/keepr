using System.Buffers.Text;
using System.Security.Cryptography;
using Keepr.Api.Data;
using Keepr.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Auth;

public class AuthSessionOptions
{
    public const string SectionName = "Session";

    /// <summary>Idle window. A session dies this long after its last request.</summary>
    public int LifetimeDays { get; set; } = 30;

    /// <summary>
    /// How stale <see cref="Session.LastSeenAt"/> may get before a request bothers to write it
    /// back. Keeps reads off the write path — see docs/cookie-session-design.md (§4.1).
    /// </summary>
    public int RenewAfterHours { get; set; } = 24;

    /// <summary>The cookie name. Not <c>__Host-</c> prefixed; see Q-C1.</summary>
    public string CookieName { get; set; } = "keepr_session";
}

/// <summary>
/// Issues, validates, and revokes login sessions.
///
/// Sessions are opaque random tokens rather than JWTs so that they can actually be revoked:
/// a JWT stays valid until it expires regardless of what the server wants.
/// </summary>
public class SessionService(AppDbContext db, IOptions<AuthSessionOptions> options, TimeProvider clock)
{
    private readonly AuthSessionOptions _opt = options.Value;

    /// <summary>
    /// Creates a session and returns the raw token to put in the cookie. The raw value is
    /// returned once and never stored — only its digest goes to the database.
    /// </summary>
    public async Task<string> IssueAsync(User user, string? userAgent, string? ip, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // Dead rows for this user are cheap to clear here and need no background sweeper: they
        // accrue about one per login, so the cleanup keeps pace with what creates them (§4.2).
        await db.Sessions
            .Where(s => s.UserId == user.Id && (s.ExpiresAt <= now || s.RevokedAt != null))
            .ExecuteDeleteAsync(ct);

        var token = GenerateToken();
        db.Sessions.Add(new Session
        {
            UserId = user.Id,
            TokenHash = Hash(token),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.AddDays(_opt.LifetimeDays),
            UserAgent = Truncate(userAgent, 512),
            CreatedIp = Truncate(ip, 45)
        });
        await db.SaveChangesAsync(ct);

        return token;
    }

    /// <summary>
    /// Resolves a cookie value to its user, sliding the expiry forward when it has drifted far
    /// enough to be worth a write. Returns null for unknown, expired, or revoked tokens — the
    /// caller cannot tell which, and does not need to.
    /// </summary>
    public async Task<User?> ValidateAsync(string token, CancellationToken ct)
    {
        var hash = Hash(token);
        var session = await db.Sessions
            .Include(s => s.User)
            .SingleOrDefaultAsync(s => s.TokenHash == hash, ct);

        var now = clock.GetUtcNow();
        if (session is null || !session.IsActive(now)) return null;

        if (session.NeedsRenewal(now, TimeSpan.FromHours(_opt.RenewAfterHours)))
        {
            session.LastSeenAt = now;
            session.ExpiresAt = now.AddDays(_opt.LifetimeDays);
            await db.SaveChangesAsync(ct);
        }

        return session.User;
    }

    /// <summary>Ends one session. Idempotent: an unknown or already-revoked token is a no-op.</summary>
    public async Task RevokeAsync(string token, CancellationToken ct)
    {
        var hash = Hash(token);
        await db.Sessions
            .Where(s => s.TokenHash == hash && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct);
    }

    /// <summary>256 bits from a CSPRNG: not guessable, so it needs no slow hash on the way in.</summary>
    private static string GenerateToken() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static byte[] Hash(string token) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null :
        value.Length <= max ? value : value[..max];
}
