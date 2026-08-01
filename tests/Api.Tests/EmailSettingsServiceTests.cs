using System.Text;
using Keepr.Api.Features.Email;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Api.Tests;

/// <summary>
/// The pure (DB-free) parts of <see cref="EmailSettingsService"/>: it seals API keys so ciphertext
/// never resembles the plaintext and round-trips under the right Data Protection purpose, and it
/// tells "email is on" from the env SMTP fallback. See docs/feature-36-email-providers.md §4/§5.
/// </summary>
public class EmailSettingsServiceTests
{
    private static EmailSettingsService Service(IDataProtectionProvider dp, EmailOptions? env = null) =>
        // db is unused by the members under test; the DB-backed paths are covered by live verification.
        new(null!, dp, Options.Create(env ?? new EmailOptions()));

    [Fact]
    public void Encrypt_round_trips_under_the_settings_purpose()
    {
        var dp = new EphemeralDataProtectionProvider();
        var service = Service(dp);

        var cipher = service.Encrypt("re_live_secret_key");

        // Ciphertext must not be the plaintext...
        Assert.NotEqual("re_live_secret_key", Encoding.UTF8.GetString(cipher));
        // ...and unsealing with the same purpose recovers it.
        var recovered = Encoding.UTF8.GetString(
            dp.CreateProtector(EmailSettingsService.ProtectorPurpose).Unprotect(cipher));
        Assert.Equal("re_live_secret_key", recovered);
    }

    [Fact]
    public void A_different_purpose_cannot_unseal_the_key()
    {
        var dp = new EphemeralDataProtectionProvider();
        var cipher = Service(dp).Encrypt("secret");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => dp.CreateProtector("some.other.purpose").Unprotect(cipher));
    }

    [Theory]
    [InlineData("smtp", true)]
    [InlineData("SMTP", true)]
    [InlineData("resend", false)]  // env never carries a hosted provider — those live in the DB
    [InlineData("none", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EnvSmtpEnabled_is_true_only_for_env_smtp(string? provider, bool expected)
    {
        var service = Service(new EphemeralDataProtectionProvider(), new EmailOptions { Provider = provider! });
        Assert.Equal(expected, service.EnvSmtpEnabled);
    }
}
