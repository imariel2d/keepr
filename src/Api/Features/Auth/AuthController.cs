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
/// <paramref name="Role"/> ("User"/"Admin") lets the client gate admin-only UI — the server still
/// enforces access on every /api/admin call, so this is for presentation, not security.
/// </summary>
public record SessionResponse(string Email, string Role);

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    SessionService sessions,
    SessionCookie cookie,
    IRegistrationGate registrationGate,
    CredentialValidator credentials,
    IOptions<QuotaOptions> quota) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
    {
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();

        // Gate before touching anything else. Checking the address first would turn this endpoint
        // into an "is this email registered?" oracle for anyone without an invite; validating
        // first would let an uninvited caller drive our outbound breach-check requests.
        var decision = await registrationGate.EvaluateAsync(
            new RegistrationAttempt(email, req.InviteCode), ct);
        if (!decision.Allowed)
            return Problem(decision.Reason, statusCode: decision.StatusCode);

        if (await credentials.ValidateAsync(email, req.Password, ct) is { } errors)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = string.Join(" ", errors.Values.SelectMany(v => v))
            });

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
        // A null PasswordHash is an invited-but-unclaimed account (§8.1): it has no password to
        // verify, so it cannot sign in until claimed. Reported as invalid credentials, not "claim
        // your account", so login stays a poor oracle for account state.
        if (user is null || user.PasswordHash is null
            || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Problem("Invalid credentials.", statusCode: StatusCodes.Status401Unauthorized);

        // Serialize issuing a session against a concurrent kick on the *same user row*. Without
        // this, a login that passed the deletion check could insert its session just after
        // KickUser revoked all existing ones, leaving the kicked account with a live session.
        // Both paths take FOR UPDATE on the row, so they cannot interleave: either the kick's
        // revocation runs after this session exists (and revokes it too), or the re-read below —
        // under the lock, hence AsNoTracking for the row's committed value — sees the account
        // already marked and refuses. See docs/feature-34-admin-console.md §4.2.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Users
            .FromSqlRaw($"SELECT * FROM {AppDbContext.Schema}.\"Users\" WHERE \"Id\" = {{0}} FOR UPDATE", user.Id)
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        // A kicked account still has a row until the background wipe removes it (and the wipe may
        // have removed it between the read above and this lock). Either way, refuse — reported as
        // invalid credentials, not "account removed", so the endpoint stays a poor oracle for
        // account state.
        if (locked is null || locked.DeletionRequestedAt is not null)
            return Problem("Invalid credentials.", statusCode: StatusCodes.Status401Unauthorized);

        var result = await StartSession(locked, ct);
        await tx.CommitAsync(ct);
        return result;
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
        new SessionResponse(
            User.FindFirst(KeeprClaims.Email)?.Value ?? string.Empty,
            User.FindFirst(KeeprClaims.Role)?.Value ?? nameof(Role.User));

    private async Task<IActionResult> StartSession(User user, CancellationToken ct)
    {
        var token = await sessions.IssueAsync(
            user,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct);

        cookie.Set(Response, token);
        return Ok(new SessionResponse(user.Email, user.Role.ToString()));
    }
}

public class QuotaOptions
{
    public const string SectionName = "Quota";
    public long DefaultBytes { get; set; } = 5L * 1024 * 1024 * 1024;
}
