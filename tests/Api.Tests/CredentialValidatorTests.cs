using Keepr.Api.Features.Auth;

namespace Api.Tests;

/// <summary>
/// The shared email/password validation used by registration, admin-provisioned accounts, invite
/// claims, and change-password. The individual rules live in <see cref="EmailPolicy"/> /
/// <see cref="PasswordPolicy"/> (tested there); this pins that the validator wires them together and
/// reports failures under the right field keys. See docs/feature-36-account-provisioning.md §4.3.
/// </summary>
public class CredentialValidatorTests
{
    private sealed class FakeBreachCheck(bool breached) : IBreachedPasswordCheck
    {
        public Task<bool> IsBreachedAsync(string password, CancellationToken ct) => Task.FromResult(breached);
    }

    private static CredentialValidator Validator(bool breached = false) => new(new FakeBreachCheck(breached));

    private const string GoodEmail = "alex@example.com";
    private const string GoodPassword = "correct horse battery staple";

    [Fact]
    public async Task Accepts_a_good_email_and_password()
    {
        Assert.Null(await Validator().ValidateAsync(GoodEmail, GoodPassword, CancellationToken.None));
    }

    [Fact]
    public async Task Flags_a_malformed_email_under_the_email_key()
    {
        var errors = await Validator().ValidateAsync("not-an-email", GoodPassword, CancellationToken.None);
        Assert.NotNull(errors);
        Assert.Contains("email", errors!.Keys);
    }

    [Fact]
    public async Task Flags_a_too_short_password_under_the_password_key()
    {
        var errors = await Validator().ValidateAsync(GoodEmail, "short", CancellationToken.None);
        Assert.NotNull(errors);
        Assert.Contains("password", errors!.Keys);
    }

    [Fact]
    public async Task Flags_a_breached_password()
    {
        var errors = await Validator(breached: true).ValidateAsync(GoodEmail, GoodPassword, CancellationToken.None);
        Assert.NotNull(errors);
        Assert.Contains("password", errors!.Keys);
    }

    [Fact]
    public async Task Password_only_validation_accepts_a_good_password()
    {
        Assert.Null(await Validator().ValidatePasswordAsync(GoodPassword, GoodEmail, CancellationToken.None));
    }

    [Fact]
    public async Task Password_only_validation_never_reports_an_email_error()
    {
        var errors = await Validator().ValidatePasswordAsync("short", GoodEmail, CancellationToken.None);
        Assert.NotNull(errors);
        Assert.Contains("password", errors!.Keys);
        Assert.DoesNotContain("email", errors.Keys);
    }
}
