using Keepr.Api.Domain;

namespace Api.Tests;

/// <summary>
/// The rule that decides whether an invite can still be claimed — a pure function of the clock,
/// like the session and share-link liveness checks. The persistence paths (resolve, claim, resend)
/// are covered against the dockerised stack. See docs/feature-36-account-provisioning.md §8.
/// </summary>
public class AccountInviteTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static AccountInvite At(DateTimeOffset expires, DateTimeOffset? claimed = null) =>
        new() { ExpiresAt = expires, ClaimedAt = claimed };

    [Fact]
    public void Unclaimed_and_unexpired_is_claimable()
    {
        Assert.True(At(Now.AddDays(7)).IsClaimable(Now));
    }

    [Fact]
    public void Is_dead_the_instant_it_expires()
    {
        // Strict comparison: no one-tick window where an expired invite still resolves.
        Assert.False(At(Now).IsClaimable(Now));
        Assert.False(At(Now.AddTicks(-1)).IsClaimable(Now));
    }

    [Fact]
    public void Claimed_invite_is_spent_even_before_expiry()
    {
        Assert.False(At(Now.AddDays(7), claimed: Now.AddMinutes(-1)).IsClaimable(Now));
    }
}
