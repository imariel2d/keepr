using System.Buffers.Text;
using System.Text;
using Keepr.Api.Services;

namespace Api.Tests;

/// <summary>
/// <see cref="SecureToken"/> mints the single-use bearer tokens behind account invites (and session
/// cookies) and stores only their hash. These pin the two security-relevant properties: tokens are
/// fresh and URL-safe, and the stored hash is a deterministic digest that never reveals the token.
/// See docs/feature-36-account-provisioning.md §8.2 and docs/testing-strategy.md.
/// </summary>
public class SecureTokenTests
{
    [Fact]
    public void Generate_returns_a_fresh_url_safe_256_bit_token()
    {
        var a = SecureToken.Generate();
        var b = SecureToken.Generate();

        Assert.NotEqual(a, b); // two draws don't collide
        // URL-safe base64 (no '+', '/', or '=' padding) so it drops straight into a link.
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('=', a);
        // Decodes back to exactly 32 bytes of entropy.
        Assert.True(Base64Url.TryDecodeFromChars(a, new byte[32], out var written));
        Assert.Equal(32, written);
    }

    [Fact]
    public void Hash_is_a_deterministic_32_byte_digest_that_is_not_the_token()
    {
        const string token = "some-single-use-token";

        var digest = SecureToken.Hash(token);

        Assert.Equal(32, digest.Length); // SHA-256
        Assert.Equal(digest, SecureToken.Hash(token)); // same input → same digest (lookup works)
        Assert.NotEqual(token, Encoding.UTF8.GetString(digest)); // the raw token never survives
    }

    [Fact]
    public void Hash_differs_for_different_tokens()
    {
        Assert.NotEqual(SecureToken.Hash("token-a"), SecureToken.Hash("token-b"));
    }
}
