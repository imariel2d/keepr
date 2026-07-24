using System.Text;
using Keepr.Api.Features.Auth;

namespace Api.Tests;

public class PasswordPolicyTests
{
    private const string Email = "ariel@example.com";

    [Theory]
    [InlineData("correct horse battery staple")]  // spaces allowed, no composition rules
    [InlineData("aaaaaaaaaaaa")]                  // 12 chars, no variety required
    [InlineData("^&*()_+{}|:<>?~")]
    public void Accepts_passwords_that_meet_the_length_rule(string password)
    {
        Assert.Empty(PasswordPolicy.Validate(password, Email));
    }

    /// <summary>
    /// The reported case: "welcome1" was accepted because nothing enforced a minimum length.
    /// </summary>
    [Fact]
    public void Rejects_the_reported_weak_password()
    {
        Assert.Contains(PasswordPolicy.TooShortMessage, PasswordPolicy.Validate("welcome1", Email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("elevenchars")]   // 11 — one below the limit
    public void Rejects_passwords_below_the_minimum(string? password)
    {
        Assert.Contains(PasswordPolicy.TooShortMessage, PasswordPolicy.Validate(password, Email));
    }

    [Fact]
    public void Twelve_characters_is_accepted()
    {
        Assert.Equal(12, "twelvechars!".Length);
        Assert.Empty(PasswordPolicy.Validate("twelvechars!", Email));
    }

    [Fact]
    public void Accepts_a_password_exactly_at_the_byte_limit()
    {
        Assert.Empty(PasswordPolicy.Validate(new string('a', 72), Email));
    }

    /// <summary>
    /// Rejected loudly rather than truncated silently: BCrypt would hash only the first 72 bytes,
    /// so the rest of the password would be decorative.
    /// </summary>
    [Fact]
    public void Rejects_a_password_one_byte_over_the_limit()
    {
        Assert.Contains(PasswordPolicy.TooLongMessage, PasswordPolicy.Validate(new string('a', 73), Email));
    }

    /// <summary>
    /// The limit is bytes, not characters — the case a naive `password.Length` check would miss.
    /// </summary>
    [Fact]
    public void Rejects_a_multibyte_password_that_is_short_in_characters()
    {
        // 19 emoji: 19 text elements the user sees, but 4 bytes each — 76 in total.
        var password = string.Concat(Enumerable.Repeat("😀", 19));

        Assert.Equal(19, new System.Globalization.StringInfo(password).LengthInTextElements);
        Assert.True(Encoding.UTF8.GetByteCount(password) > PasswordPolicy.MaxBytes);
        Assert.Contains(PasswordPolicy.TooLongMessage, PasswordPolicy.Validate(password, Email));
    }

    [Theory]
    [InlineData("ariel@example.com!!")]   // the whole address
    [InlineData("myarielpassword")]       // the local part
    [InlineData("MYARIELPASSWORD")]       // case-insensitively
    public void Rejects_a_password_containing_the_email(string password)
    {
        Assert.Contains(PasswordPolicy.ContainsEmailMessage, PasswordPolicy.Validate(password, Email));
    }

    /// <summary>
    /// A very short local part would otherwise ban every password containing those letters —
    /// "ann@x.com" must not reject "channelsurfing".
    /// </summary>
    [Fact]
    public void Short_local_parts_do_not_trigger_the_containment_rule()
    {
        Assert.Empty(PasswordPolicy.Validate("channelsurfing", "an@example.com"));
    }

    [Fact]
    public void Reports_every_failure_at_once()
    {
        // Too short *and* contains the email.
        var errors = PasswordPolicy.Validate("ariel", Email);

        Assert.Contains(PasswordPolicy.TooShortMessage, errors);
        Assert.Contains(PasswordPolicy.ContainsEmailMessage, errors);
    }
}
