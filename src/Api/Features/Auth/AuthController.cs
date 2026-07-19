using Media.Api.Data;
using Media.Api.Domain;
using Media.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Media.Api.Features.Auth;

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string AccessToken);

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    JwtTokenService tokens,
    IOptions<QuotaOptions> quota) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict(new { error = "Email already registered." });

        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            QuotaBytes = quota.Value.DefaultBytes
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return Ok(new AuthResponse(tokens.CreateAccessToken(user)));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid credentials." });

        return Ok(new AuthResponse(tokens.CreateAccessToken(user)));
    }
}

public class QuotaOptions
{
    public const string SectionName = "Quota";
    public long DefaultBytes { get; set; } = 5L * 1024 * 1024 * 1024;
}
