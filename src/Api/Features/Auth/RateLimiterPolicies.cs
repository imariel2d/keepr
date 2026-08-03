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
    /// per client IP. See docs/feature-26-password-reset.md §5.1.</summary>
    public const string ForgotPassword = "forgot-password";
}
