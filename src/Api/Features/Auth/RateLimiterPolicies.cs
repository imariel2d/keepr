using System.Security.Claims;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Keepr.Api.Features.Auth;

/// <summary>
/// Named rate-limiter policies, registered in <c>Program.cs</c> and applied to actions with
/// <c>[EnableRateLimiting(...)]</c>. Keeping the names in one place stops the registration and the
/// attribute from drifting apart.
/// </summary>
public static class RateLimiterPolicies
{
    /// <summary>Throttles <c>POST /api/auth/forgot-password</c> — the one public, unauthenticated,
    /// email-triggering endpoint — to blunt account enumeration and outbound-mail abuse. Partitioned
    /// per client IP (<see cref="ClientPartitionKey"/>). See docs/feature-26-password-reset.md §5.1.</summary>
    public const string ForgotPassword = "forgot-password";

    /// <summary>Throttles <c>POST /api/me/email</c> — the authenticated, outbound-mail-triggering
    /// change-email request — to cap confirmation-mail fan-out. Partitioned <b>per user</b>
    /// (<see cref="UserPartitionKey"/>) rather than per IP: the endpoint is authenticated, so the
    /// account is the natural bucket. See docs/feature-27-change-email.md §5.1.</summary>
    public const string ChangeEmail = "change-email";

    /// <summary>
    /// The per-client key the limiter partitions on. Behind App Platform's proxy,
    /// <c>Connection.RemoteIpAddress</c> is the <b>proxy</b>, so keying on it would put every client in
    /// one bucket — a single abuser would then <c>429</c> everyone. Prefer the <b>rightmost</b>
    /// <c>X-Forwarded-For</c> entry: with a single trusted proxy hop that is the address the proxy
    /// appended, and a public client can't forge it (an injected value sits to the <i>left</i> of the
    /// proxy's). Falls back to the socket IP off-platform. A deployment behind additional proxy hops
    /// must revisit this. See docs/feature-26-password-reset.md §5.1.
    /// </summary>
    public static string ClientPartitionKey(HttpContext http)
    {
        var xff = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(xff))
        {
            var parts = xff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0) return parts[^1];
        }
        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>The per-user key for authenticated limiters: the "sub" claim (the account id). Auth
    /// middleware runs before the rate limiter (Program.cs pipeline order), so it's populated on a
    /// signed-in request; an unauthenticated caller (which the endpoint's <c>[Authorize]</c> will 401
    /// anyway) falls back to the client IP so it can't share a bucket with signed-in users.</summary>
    public static string UserPartitionKey(HttpContext http)
    {
        var sub = http.User.FindFirstValue(KeeprClaims.Sub);
        return string.IsNullOrEmpty(sub) ? ClientPartitionKey(http) : $"user:{sub}";
    }
}
