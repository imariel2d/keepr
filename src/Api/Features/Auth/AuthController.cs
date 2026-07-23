using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Auth;

/// <param name="Email">Address to register; matched case-insensitively.</param>
/// <param name="Password">Plaintext password, hashed with BCrypt before storage.</param>
/// <param name="InviteCode">Required: signups are gated, see <see cref="IRegistrationGate"/>.</param>
public record RegisterRequest(string Email, string Password, string? InviteCode);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string AccessToken);

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    JwtTokenService tokens,
    IRegistrationGate registrationGate,
    IOptions<QuotaOptions> quota) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        // Gate before touching the database. Checking the address first would turn this endpoint
        // into an "is this email registered?" oracle for anyone without an invite.
        var decision = await registrationGate.EvaluateAsync(
            new RegistrationAttempt(email, req.InviteCode), ct);
        if (!decision.Allowed)
            return Problem(decision.Reason, statusCode: decision.StatusCode);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Problem("Email already registered.", statusCode: StatusCodes.Status409Conflict);

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
            return Problem("Invalid credentials.", statusCode: StatusCodes.Status401Unauthorized);

        return Ok(new AuthResponse(tokens.CreateAccessToken(user)));
    }
}

public class QuotaOptions
{
    public const string SectionName = "Quota";
    public long DefaultBytes { get; set; } = 5L * 1024 * 1024 * 1024;
}
