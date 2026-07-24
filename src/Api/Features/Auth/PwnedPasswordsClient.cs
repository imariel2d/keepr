using System.Security.Cryptography;
using System.Text;

namespace Keepr.Api.Features.Auth;

public interface IBreachedPasswordCheck
{
    Task<bool> IsBreachedAsync(string password, CancellationToken ct);
}

/// <summary>
/// Checks a password against Have I Been Pwned's Pwned Passwords corpus using k-anonymity.
///
/// The password never leaves this process, and neither does its full hash: only the first five hex
/// characters of the SHA-1 digest are sent, and the server returns every suffix sharing that
/// prefix (~800 of them) for local comparison. HIBP cannot tell which one was being asked about.
///
/// SHA-1 here is not a security choice — it is the corpus's index format. The digest is a lookup
/// key against a public list, never a stored credential.
/// </summary>
public class PwnedPasswordsClient(HttpClient http, ILogger<PwnedPasswordsClient> log)
    : IBreachedPasswordCheck
{
    public async Task<bool> IsBreachedAsync(string password, CancellationToken ct)
    {
        var digest = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = digest[..5];
        var suffix = digest[5..];

        try
        {
            using var response = await http.GetAsync($"range/{prefix}", ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                // Each line is "SUFFIX:count"; the count is not used, since appearing at all is
                // disqualifying.
                var separator = line.IndexOf(':');
                var candidate = separator < 0 ? line : line[..separator];
                if (candidate.Equals(suffix, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fail OPEN, unlike the invite gate's fail-closed. The difference is who is at fault:
            // a missing invite code is a misconfiguration we control and should shout about, while
            // HIBP being unreachable is a third party's outage. Failing closed there would let
            // someone else's downtime shut off registration entirely. The length rules still
            // apply, so this degrades to a weaker policy rather than to none.
            log.LogWarning(ex, "Pwned Passwords check failed; allowing the password on this attempt.");
            return false;
        }
    }
}
