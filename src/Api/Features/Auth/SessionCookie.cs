using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Auth;

/// <summary>
/// Writes and clears the session cookie. Kept apart from <see cref="SessionService"/> so the
/// attributes that make the cookie safe live in exactly one place and can be asserted in tests.
/// </summary>
public class SessionCookie(IOptions<AuthSessionOptions> options, IWebHostEnvironment env)
{
    private readonly AuthSessionOptions _opt = options.Value;

    public string Name => _opt.CookieName;

    public void Set(HttpResponse response, string token) =>
        response.Cookies.Append(_opt.CookieName, token, Options(persistent: true));

    /// <summary>
    /// Deletion has to repeat the same attributes it was written with — a browser matches a
    /// delete to an existing cookie by name/path/domain, so a mismatch silently leaves it in place.
    /// </summary>
    public void Clear(HttpResponse response) =>
        response.Cookies.Delete(_opt.CookieName, Options(persistent: false));

    private CookieOptions Options(bool persistent) => new()
    {
        HttpOnly = true,

        // Lax, not Strict: Strict also withholds the cookie when the user follows an external link
        // into the app, which would bounce a logged-in user to /login until they reloaded. Lax
        // still blocks every cross-site fetch and every cross-site POST/PATCH/DELETE, which is
        // every state-changing endpoint here. See docs/cookie-session-design.md (§5).
        SameSite = SameSiteMode.Lax,

        // Local development is served over plain http, where a Secure cookie would be dropped.
        Secure = !env.IsDevelopment(),

        Path = "/",

        // Persistent, or the session would end with the browser process and "keep me signed in"
        // would be a lie. The server-side row is still the authority on expiry.
        Expires = persistent ? DateTimeOffset.UtcNow.AddDays(_opt.LifetimeDays) : null,

        // Exempt from cookie-consent suppression: without it there is no way to log in at all.
        IsEssential = true
    };
}
