using Keepr.Api.Features.Auth;

namespace Api.Tests;

/// <summary>
/// Since #36 public self-registration is closed: the wired gate refuses every attempt regardless of
/// what secret is supplied, so <c>POST /api/auth/register</c> is a permanently-shut door while the
/// endpoint stays in place. See docs/feature-36-account-provisioning.md §3.1.
/// </summary>
public class ClosedRegistrationGateTests
{
    private static readonly ClosedRegistrationGate Gate = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("any-code")]
    public async Task Denies_every_attempt(string? secret)
    {
        var decision = await Gate.EvaluateAsync(
            new RegistrationAttempt("someone@example.com", secret), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
    }
}
