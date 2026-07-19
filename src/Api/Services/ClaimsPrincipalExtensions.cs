using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Keepr.Api.Services;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated user's id from the JWT "sub" claim.</summary>
    public static Guid UserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new UnauthorizedAccessException("No subject claim.");
        return Guid.Parse(sub);
    }
}
