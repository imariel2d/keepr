using System.Security.Claims;

namespace Keepr.Api.Services;

/// <summary>
/// Claim names issued by the session authentication handler.
///
/// These are the JWT registered names ("sub", "email") kept verbatim through the move from JWTs to
/// cookie sessions: the values are conventional and self-describing, and reusing them meant no
/// controller had to change. Declared here rather than taken from <c>JwtRegisteredClaimNames</c>
/// so the app no longer drags in a JWT package for two strings.
/// </summary>
public static class KeeprClaims
{
    public const string Sub = "sub";
    public const string Email = "email";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated user's id from the "sub" claim.</summary>
    public static Guid UserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(KeeprClaims.Sub)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new UnauthorizedAccessException("No subject claim.");
        return Guid.Parse(sub);
    }
}
