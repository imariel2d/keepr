using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Auth;

/// <param name="Email">Address to register; matched case-insensitively.</param>
/// <param name="Password">Plaintext password, hashed with BCrypt before storage.</param>
/// <param name="InviteCode">Required: signups are gated, see <see cref="IRegistrationGate"/>.</param>
public record RegisterRequest(string Email, string Password, string? InviteCode);
public record LoginRequest(string Email, string Password);

/// <summary>
/// Who the caller is. Carries no credential: the session lives in an HttpOnly cookie that
/// JavaScript cannot read, so this is the client's only way to answer "am I signed in?".
/// </summary>
public record SessionResponse(string Email);

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    SessionService sessions,
    SessionCookie cookie,
    IRegistrationGate registrationGate,
    IOptions<QuotaOptions> quota) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
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

        return await StartSession(user, ct);
    }

    [HttpPost("login")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Problem("Invalid credentials.", statusCode: StatusCodes.Status401Unauthorized);

        return await StartSession(user, ct);
    }

    /// <summary>
    /// Ends the current session server-side and clears the cookie.
    ///
    /// This has to be a server call: the cookie is HttpOnly, so the client physically cannot
    /// delete it, and dropping only the client's state would leave the session fully usable.
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        // Deliberately anonymous and always 204: logging out is idempotent, and an expired session
        // must still be able to clear its own cookie.
        var token = Request.Cookies[cookie.Name];
        if (!string.IsNullOrEmpty(token))
            await sessions.RevokeAsync(token, ct);

        cookie.Clear(Response);
        return NoContent();
    }

    /// <summary>Resolves the current session. The client calls this on startup to decide routing.</summary>
    [HttpGet("session")]
    [Authorize]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<SessionResponse> Current() =>
        new SessionResponse(User.FindFirst(KeeprClaims.Email)?.Value ?? string.Empty);

    private async Task<IActionResult> StartSession(User user, CancellationToken ct)
    {
        var token = await sessions.IssueAsync(
            user,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct);

        cookie.Set(Response, token);
        return Ok(new SessionResponse(user.Email));
    }
}

public class QuotaOptions
{
    public const string SectionName = "Quota";
    public long DefaultBytes { get; set; } = 5L * 1024 * 1024 * 1024;
}
