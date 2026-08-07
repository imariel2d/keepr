using Keepr.Api.Features.Localization;

namespace Keepr.Api.Http;

/// <summary>
/// Picks which per-locale SPA build (wwwroot/{en,es,fr}) to serve for a request that arrives without
/// a locale prefix (#30). The client ships one build per locale under /{locale}/; this decides where
/// the bare root "/" redirects to. See docs/feature-30-localization.md §4.3.
/// </summary>
public static class LocalePicker
{
    /// <summary>Cookie the client sets on an explicit language choice; read here only to pick the
    /// redirect target, never to change the account's stored preference.</summary>
    public const string Cookie = "keepr_lang";

    /// <summary>The locales that have a build in wwwroot. Same set as the client + the API validator.</summary>
    public static IReadOnlyCollection<string> Supported => SupportedLanguages.Codes;

    /// <summary>
    /// The locale to serve: the <c>keepr_lang</c> cookie when it names a supported locale, otherwise
    /// the default (English). <c>Accept-Language</c> is deliberately <b>not</b> consulted — the
    /// default is always English until the user explicitly picks a language (Q-30-3).
    /// </summary>
    public static string Pick(HttpContext ctx)
    {
        var cookie = ctx.Request.Cookies[Cookie];
        return cookie is not null && SupportedLanguages.IsSupported(cookie)
            ? cookie
            : SupportedLanguages.Default;
    }
}
