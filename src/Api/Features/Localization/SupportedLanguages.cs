namespace Keepr.Api.Features.Localization;

/// <summary>
/// The canonical set of UI locales the app is localized into, and the pure validation the API uses
/// when an account sets its preferred language (#30). English is the source locale and the default
/// when a preference is unset (null). Kept deliberately small and dependency-free so it can be unit
/// tested like <c>EmailPolicy</c> / <c>PasswordPolicy</c>. Mirrors the client's
/// <c>core/locale.ts</c> — the two lists must stay in step (the i18n-translations skill).
/// See docs/feature-30-localization.md §3.
/// </summary>
public static class SupportedLanguages
{
    /// <summary>The locale served when a preference is unset (null). Also the i18n source locale.</summary>
    public const string Default = "en";

    /// <summary>Every supported locale code, lowercase. Adding a language is a one-line change here
    /// (plus a client catalog + build).</summary>
    public static readonly IReadOnlySet<string> Codes =
        new HashSet<string>(StringComparer.Ordinal) { "en", "es", "fr" };

    /// <summary>True when <paramref name="code"/> is exactly one of <see cref="Codes"/> (already
    /// normalized — lowercase, trimmed).</summary>
    public static bool IsSupported(string code) => Codes.Contains(code);

    /// <summary>
    /// Normalizes a raw preferred-language input from the client and reports whether it's acceptable.
    /// Trims and lowercases; a blank/whitespace value becomes <c>null</c> (a valid "unset → default"
    /// state). Returns <c>false</c> only when a non-blank value isn't a supported code — the caller
    /// maps that to a 400 <c>invalid_language</c>.
    /// </summary>
    /// <param name="raw">The value as received (may be null, blank, mixed-case, or padded).</param>
    /// <param name="normalized">The canonical code to persist, or <c>null</c> to clear the preference.</param>
    public static bool TryNormalize(string? raw, out string? normalized)
    {
        var trimmed = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed))
        {
            normalized = null;
            return true;
        }

        normalized = trimmed;
        return IsSupported(trimmed);
    }
}
