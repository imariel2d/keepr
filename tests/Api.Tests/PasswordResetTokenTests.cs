using Keepr.Api.Domain;

namespace Api.Tests;

/// <summary>
/// The rule that decides whether a reset token can still be used — a pure function of the clock,
/// like the invite and session liveness checks. The persistence paths (resolve, complete, revoke-all,
/// admin reset) are covered against the dockerised stack. See docs/feature-26-password-reset.md §4.
/// </summary>
public class PasswordResetTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static PasswordResetToken At(DateTimeOffset expires, DateTimeOffset? used = null) =>
        new() { ExpiresAt = expires, UsedAt = used };

    [Fact]
    public void Unused_and_unexpired_is_usable()
    {
        Assert.True(At(Now.AddMinutes(60)).IsUsable(Now));
    }

    [Fact]
    public void Is_dead_the_instant_it_expires()
    {
        // Strict comparison: no one-tick window where an expired token still resolves.
        Assert.False(At(Now).IsUsable(Now));
        Assert.False(At(Now.AddTicks(-1)).IsUsable(Now));
    }

    [Fact]
    public void Used_token_is_spent_even_before_expiry()
    {
        Assert.False(At(Now.AddMinutes(60), used: Now.AddMinutes(-1)).IsUsable(Now));
    }
}
