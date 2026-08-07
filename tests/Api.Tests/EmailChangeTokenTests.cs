using Keepr.Api.Domain;
using Keepr.Api.Features.Auth;

namespace Api.Tests;

/// <summary>
/// The rule that decides whether an email-change confirmation token can still be used — a pure
/// function of the clock, like the reset/invite/session liveness checks. The persistence paths
/// (resolve, confirm, supersede, cancel) are covered against the dockerised stack. See
/// docs/feature-27-change-email.md §4.
/// </summary>
public class EmailChangeTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private static EmailChangeToken At(DateTimeOffset expires, DateTimeOffset? used = null) =>
        new() { NewEmail = "new@example.com", ExpiresAt = expires, UsedAt = used };

    [Fact]
    public void Unused_and_unexpired_is_usable()
    {
        Assert.True(At(Now.AddHours(24)).IsUsable(Now));
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
        Assert.False(At(Now.AddHours(24), used: Now.AddMinutes(-1)).IsUsable(Now));
    }
}

/// <summary>
/// The old-address heads-up masks the new address so the old inbox never spells it out in full. Pure
/// string logic; see docs/feature-27-change-email.md §11.
/// </summary>
public class EmailAddressMaskTests
{
    [Fact]
    public void Keeps_first_local_char_and_the_whole_domain()
    {
        Assert.Equal("a•••@example.com", EmailChangeService.Mask("alex@example.com"));
    }

    [Fact]
    public void Single_char_local_part_reveals_nothing_of_the_local()
    {
        Assert.Equal("•••@x.io", EmailChangeService.Mask("a@x.io"));
    }

    [Fact]
    public void Malformed_input_without_an_at_is_fully_masked()
    {
        Assert.Equal("•••", EmailChangeService.Mask("notanemail"));
    }
}
