using Keepr.Api.Domain;

namespace Api.Tests;

/// <summary>
/// The two rules that decide whether a cookie still works, and how often the row behind it is
/// rewritten. Both are pure functions of the clock, so they are tested here rather than through a
/// database — see docs/cookie-session-design.md (§8) for why the persistence layer is covered
/// end-to-end instead.
/// </summary>
public class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    private static Session At(DateTimeOffset expires, DateTimeOffset? revoked = null) =>
        new() { ExpiresAt = expires, RevokedAt = revoked, LastSeenAt = Now };

    [Fact]
    public void Session_with_a_future_expiry_is_active()
    {
        Assert.True(At(Now.AddDays(30)).IsActive(Now));
    }

    [Fact]
    public void Session_is_dead_the_instant_it_expires()
    {
        // Exactly at ExpiresAt counts as expired: the comparison is strict, so there is no
        // one-tick window where a dead session still authenticates.
        Assert.False(At(Now).IsActive(Now));
        Assert.False(At(Now.AddTicks(-1)).IsActive(Now));
    }

    /// <summary>Revocation is the property a JWT could not offer: it ignores the clock entirely.</summary>
    [Fact]
    public void Revoked_session_is_dead_even_with_a_long_expiry()
    {
        Assert.False(At(Now.AddDays(30), revoked: Now.AddMinutes(-1)).IsActive(Now));
    }

    [Theory]
    [InlineData(0, false)]      // same request
    [InlineData(23, false)]     // inside the throttle window
    [InlineData(24, true)]      // exactly at it — renew
    [InlineData(72, true)]
    public void Renewal_is_throttled_to_the_configured_window(int hoursSinceLastSeen, bool expected)
    {
        var session = At(Now.AddDays(30));
        var now = Now.AddHours(hoursSinceLastSeen);

        Assert.Equal(expected, session.NeedsRenewal(now, TimeSpan.FromHours(24)));
    }
}
