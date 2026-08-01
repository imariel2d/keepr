using Keepr.Api.Features.Email;

namespace Api.Tests;

/// <summary>
/// <see cref="EmailOptions.Enabled"/> is the single source of truth for "email is on" — Program.cs
/// picks the sender by it, and the admin create path gates invite mode on it. So its handling of the
/// unset/none/whitespace cases has to be exact. See docs/feature-36-account-provisioning.md §6.
/// </summary>
public class EmailOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("NONE")]
    public void Disabled_when_provider_is_blank_or_none(string? provider)
    {
        Assert.False(new EmailOptions { Provider = provider! }.Enabled);
    }

    [Theory]
    [InlineData("smtp")]
    [InlineData("SMTP")]
    public void Enabled_when_a_real_provider_is_set(string provider)
    {
        Assert.True(new EmailOptions { Provider = provider }.Enabled);
    }
}
