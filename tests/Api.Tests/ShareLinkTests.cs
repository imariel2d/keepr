using Keepr.Api.Domain;

namespace Api.Tests;

/// <summary>
/// The rule that decides whether a link still grants access. It is a pure function of the clock,
/// so it is tested here directly; the service's persistence paths are covered end-to-end (they
/// lean on Postgres-only ExecuteUpdate/DateTimeOffset translation, like the session cleanup).
/// See docs/feature-7-shareable-links.md.
/// </summary>
public class ShareLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private static ShareLink At(DateTimeOffset expires, DateTimeOffset? revoked = null) =>
        new() { ExpiresAt = expires, RevokedAt = revoked };

    [Fact]
    public void Link_with_a_future_expiry_is_live()
    {
        Assert.True(At(Now.AddDays(7)).IsLive(Now));
    }

    [Fact]
    public void Link_is_dead_the_instant_it_expires()
    {
        // Strict comparison: no one-tick window where an expired link still resolves.
        Assert.False(At(Now).IsLive(Now));
        Assert.False(At(Now.AddTicks(-1)).IsLive(Now));
    }

    [Fact]
    public void Revoked_link_is_dead_even_with_a_long_expiry()
    {
        // Revocation ignores the clock — the whole point of "stop sharing".
        Assert.False(At(Now.AddDays(30), revoked: Now.AddMinutes(-1)).IsLive(Now));
    }

    [Fact]
    public void Link_that_never_expires_is_live_regardless_of_the_clock()
    {
        // A null expiry means "no expiry" — never lapses, no matter how far the clock advances.
        var link = new ShareLink { ExpiresAt = null };
        Assert.True(link.IsLive(Now));
        Assert.True(link.IsLive(Now.AddYears(100)));
    }

    [Fact]
    public void Never_expiring_link_is_still_dead_once_revoked()
    {
        // Revocation is terminal even for a link that would otherwise never expire.
        var link = new ShareLink { ExpiresAt = null, RevokedAt = Now.AddMinutes(-1) };
        Assert.False(link.IsLive(Now));
    }
}
