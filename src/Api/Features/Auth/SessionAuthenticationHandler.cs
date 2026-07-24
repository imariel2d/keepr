using System.Security.Claims;
using System.Text.Encodings.Web;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Auth;

/// <summary>
/// Authenticates requests from the session cookie set at login.
///
/// Emits the same <c>sub</c> claim the JWT handler did, so
/// <see cref="Services.ClaimsPrincipalExtensions.UserId"/> and every <c>[Authorize]</c> controller
/// carry on unchanged — the credential moved, the identity did not.
/// </summary>
public class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionCookie cookie,
    SessionService sessions) : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[cookie.Name];

        // NoResult, not Fail: no cookie means "anonymous", which [AllowAnonymous] endpoints and
        // the SPA's own static files rely on. Fail would turn every unauthenticated GET into an error.
        if (string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();

        var user = await sessions.ValidateAsync(token, Context.RequestAborted);
        if (user is null)
        {
            // The cookie is real but the session behind it is gone (expired, revoked, or never
            // existed). Clear it so the browser stops sending a credential that cannot work.
            // Via SessionCookie, not Response.Cookies.Delete: deletion has to repeat the same
            // attributes the cookie was written with, and that knowledge lives in one place.
            cookie.Clear(Response);
            return AuthenticateResult.Fail("Session is not valid.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(KeeprClaims.Sub, user.Id.ToString()),
                new Claim(KeeprClaims.Email, user.Email)
            ],
            SchemeName);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
