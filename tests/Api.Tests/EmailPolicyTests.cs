using Keepr.Api.Features.Auth;

namespace Api.Tests;

public class EmailPolicyTests
{
    [Theory]
    [InlineData("ariel@example.com")]
    [InlineData("a@b.co")]
    [InlineData("first.last@sub.domain.example.com")]
    [InlineData("user+tag@gmail.com")]           // plus addressing is normal, not suspicious
    [InlineData("user_name-1@example-host.org")]
    [InlineData("o'brien@example.com")]          // apostrophes are legal atext
    public void Accepts_ordinary_addresses(string email)
    {
        Assert.Null(EmailPolicy.Validate(email));
    }

    /// <summary>
    /// The reported case: "imariel@gmail" registered successfully because nothing checked that a
    /// domain has a dot. A bare hostname is not a public mail domain.
    /// </summary>
    [Theory]
    [InlineData("imariel@gmail")]
    [InlineData("user@localhost")]
    [InlineData("user@com")]
    public void Rejects_a_domain_with_no_dot(string email)
    {
        Assert.Equal(EmailPolicy.MalformedMessage, EmailPolicy.Validate(email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanemail")]
    [InlineData("@example.com")]                 // no local part
    [InlineData("user@")]                        // no domain
    [InlineData("a@b@example.com")]              // two @
    [InlineData(".user@example.com")]            // leading dot
    [InlineData("user.@example.com")]            // trailing dot
    [InlineData("us..er@example.com")]           // doubled dot
    [InlineData("user name@example.com")]        // whitespace
    [InlineData("user@example..com")]
    [InlineData("user@-example.com")]            // hyphen-initial label
    [InlineData("user@example-.com")]            // hyphen-final label
    [InlineData("user@example.c")]               // one-character TLD
    [InlineData("user@example.123")]             // numeric TLD
    [InlineData("user@[192.168.1.1]")]           // address literal: legal RFC, out of scope
    [InlineData("\"john doe\"@example.com")]     // quoted local part: same
    public void Rejects_malformed_addresses(string email)
    {
        Assert.Equal(EmailPolicy.MalformedMessage, EmailPolicy.Validate(email));
    }

    [Fact]
    public void Rejects_addresses_over_the_rfc_length_limits()
    {
        Assert.NotNull(EmailPolicy.Validate(new string('a', 65) + "@example.com"));   // local > 64
        Assert.NotNull(EmailPolicy.Validate(new string('a', 250) + "@example.com"));  // total > 254
    }

    [Theory]
    [InlineData("someone@mailinator.com")]
    [InlineData("someone@guerrillamail.com")]
    [InlineData("someone@YOPMAIL.COM")]          // matching is case-insensitive
    public void Rejects_disposable_providers(string email)
    {
        Assert.Equal(EmailPolicy.DisposableMessage, EmailPolicy.Validate(email));
    }

    /// <summary>
    /// The regression that would otherwise ship silently. Community blocklists routinely include
    /// these, but they are durable forwarding aliases used by privacy-conscious people — blocking
    /// one tells an invited user their email provider is not allowed, which is a dead end.
    /// </summary>
    [Theory]
    [InlineData("someone@simplelogin.io")]
    [InlineData("someone@addy.io")]
    [InlineData("someone@anonaddy.me")]
    [InlineData("someone@mozmail.com")]          // Firefox Relay
    [InlineData("someone@duck.com")]             // DuckDuckGo
    [InlineData("someone@icloud.com")]           // Apple Hide My Email
    [InlineData("someone@pm.me")]                // Proton
    public void Does_not_block_durable_alias_services(string email)
    {
        Assert.Null(EmailPolicy.Validate(email));
    }
}
