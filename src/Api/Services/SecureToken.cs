using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Keepr.Api.Services;

/// <summary>
/// A bearer token we hand out once and store only hashed — for account invites (and any future
/// single-use link). 256 bits from a CSPRNG is unguessable, so the stored SHA-256 needs no slow
/// hash; a table dump can't be replayed. Same construction <see cref="Features.Auth.SessionService"/>
/// uses for session cookies. See docs/feature-36-account-provisioning.md §8.2.
/// </summary>
public static class SecureToken
{
    /// <summary>A fresh URL-safe token to put in a link. Returned once; only its hash is stored.</summary>
    public static string Generate() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>The digest to store and to look a presented token up by.</summary>
    public static byte[] Hash(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
