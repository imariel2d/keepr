using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Keepr.Api.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Keepr.Api.Services;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "media-api";
    public string Audience { get; set; } = "media-app";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 60;
}

public class JwtTokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> opt)
{
    private readonly JwtOptions _opt = opt.Value;

    public string CreateAccessToken(User user)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_opt.AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
