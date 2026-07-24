using System.Collections.Frozen;
using System.Reflection;

namespace Keepr.Api.Features.Auth;

/// <summary>
/// Whether an address is well-formed enough, and permanent enough, to hang an account on.
///
/// Deliberately narrower than RFC 5322: quoted local parts (<c>"john doe"@example.com</c>),
/// address literals (<c>user@[192.168.1.1]</c>), and comments are all legal and essentially
/// nobody has one. Rejecting them keeps every downstream assumption simple. If this ever bites a
/// real user, relax one rule and add a test naming the address that motivated it.
///
/// See docs/user-registration-validation-design.md (§4).
/// </summary>
public static class EmailPolicy
{
    // RFC 5321: 254 for the whole address, 64 for the local part.
    private const int MaxTotal = 254;
    private const int MaxLocal = 64;

    /// <summary>
    /// Loaded once from the embedded snapshot. Frozen because it is read on every registration
    /// and never written after startup.
    /// </summary>
    private static readonly FrozenSet<string> Disposable = LoadDisposableDomains();

    public const string MalformedMessage = "Enter a valid email address.";
    public const string DisposableMessage =
        "That email provider isn't supported. Use a permanent address.";

    /// <summary>
    /// Validates an address that has already been trimmed and lowercased by the caller.
    /// Returns null when the address is acceptable, otherwise the user-facing reason.
    /// </summary>
    public static string? Validate(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > MaxTotal) return MalformedMessage;

        // Exactly one @, and something on both sides of it.
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1)
            return MalformedMessage;

        var local = email[..at];
        var domain = email[(at + 1)..];

        return !IsValidLocal(local) || !IsValidDomain(domain) ? MalformedMessage
            : Disposable.Contains(domain) ? DisposableMessage
            : null;
    }

    private static bool IsValidLocal(string local)
    {
        if (local.Length > MaxLocal) return false;

        // A leading, trailing, or doubled dot is invalid unquoted, and quoted local parts are
        // out of scope by design.
        if (local[0] == '.' || local[^1] == '.' || local.Contains("..")) return false;

        return local.All(IsAllowedLocalChar);
    }

    // The unquoted "atext" set from RFC 5322, plus the dot handled above. Anything outside it —
    // whitespace, control characters, quotes, brackets, commas — is rejected.
    private static bool IsAllowedLocalChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || ".!#$%&'*+/=?^_`{|}~-".Contains(c);

    private static bool IsValidDomain(string domain)
    {
        // A dot is required, which is what rejects "user@gmail" and "user@localhost". Intranet-only
        // hostnames are not something a public signup form should accept.
        if (domain.Length is 0 or > 253 || !domain.Contains('.')) return false;
        if (domain.StartsWith('.') || domain.EndsWith('.') || domain.Contains("..")) return false;

        var labels = domain.Split('.');
        if (labels.Any(l => l.Length is 0 or > 63)) return false;
        if (labels.Any(l => l[0] == '-' || l[^1] == '-')) return false;
        if (!labels.All(l => l.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) return false;

        // A real TLD is alphabetic and at least two characters, so "user@example.c" and
        // "user@example.123" don't pass.
        var tld = labels[^1];
        return tld.Length >= 2 && tld.All(char.IsAsciiLetter);
    }

    private static FrozenSet<string> LoadDisposableDomains()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("disposable-domains.txt", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);

        var domains = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#')) domains.Add(trimmed);
        }

        return domains.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
