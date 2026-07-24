using System.Text;

namespace Keepr.Api.Features.Auth;

/// <summary>
/// Password rules, following NIST SP 800-63B: length and breach-checking do the work, composition
/// rules do not. There is deliberately no "must contain an uppercase letter and a symbol" —
/// that guidance is withdrawn precisely because it shrinks the practical search space while
/// producing predictable shapes like <c>Passw0rd!</c>.
///
/// See docs/user-registration-validation-design.md (§5).
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;

    /// <summary>
    /// BCrypt's hard limit, and the reason this is enforced rather than assumed: BCrypt.Net
    /// silently truncates at 72 bytes. A hash of a 72-byte password verifies against *any* longer
    /// password sharing that prefix, so accepting one would quietly weaken the account.
    ///
    /// Bytes, not characters — non-ASCII costs 2–4 bytes each, so a passphrase can cross this
    /// well under 72 visible characters.
    /// </summary>
    public const int MaxBytes = 72;

    public static string TooShortMessage => $"Use at least {MinLength} characters.";
    public const string TooLongMessage =
        "That password is too long. Keep it under 72 bytes (about 72 characters).";
    public const string ContainsEmailMessage = "Don't use your email address in your password.";
    public const string BreachedMessage =
        "This password has appeared in a data breach. Choose another.";

    /// <summary>
    /// The rules that need no network. Returns every failure rather than the first, so the form
    /// can show the whole picture in one round-trip.
    /// </summary>
    public static List<string> Validate(string? password, string email)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add(TooShortMessage);
            return errors;
        }

        // Length in text elements, so an emoji or an accented character counts once to the user
        // even though it costs several bytes against MaxBytes below.
        if (new System.Globalization.StringInfo(password).LengthInTextElements < MinLength)
            errors.Add(TooShortMessage);

        if (Encoding.UTF8.GetByteCount(password) > MaxBytes)
            errors.Add(TooLongMessage);

        if (ContainsEmail(password, email))
            errors.Add(ContainsEmailMessage);

        return errors;
    }

    /// <summary>
    /// NIST's "context-specific words" rule, kept to the one word that is always available and
    /// always tempting: the address they are signing up with. Compares the local part, so
    /// "ariel@example.com" also rejects "ariel12345678".
    /// </summary>
    private static bool ContainsEmail(string password, string email)
    {
        if (password.Contains(email, StringComparison.OrdinalIgnoreCase)) return true;

        var at = email.IndexOf('@');
        if (at <= 0) return false;

        var local = email[..at];
        // Below three characters the local part is too short for a containment test to mean
        // anything — "ann" would ban every password with "ann" anywhere in it.
        return local.Length >= 3 && password.Contains(local, StringComparison.OrdinalIgnoreCase);
    }
}
