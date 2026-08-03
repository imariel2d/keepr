using Keepr.Api.Data;
using Keepr.Api.Features.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Features.Auth;

/// <param name="Email">The account address to send a reset link to; matched case-insensitively.</param>
public record ForgotPasswordRequest(string Email);

/// <param name="Password">The new password; validated like a registration password.</param>
public record ResetPasswordRequest(string Password);

/// <summary>The address a reset link is for, so the reset form can show who it's for.</summary>
public record ResetPreview(string Email);

/// <summary>What password recovery the deployment offers. <paramref name="SelfServiceReset"/> is true
/// only when a mail provider is configured — a <b>global</b> fact, never per-account, so it's safe to
/// serve anonymously. The login screen reads it to show either a "Forgot password?" link or the
/// "contact your admin" copy. See docs/feature-26-password-reset.md §7.</summary>
public record ResetCapabilities(bool SelfServiceReset);

/// <summary>
/// Self-service password recovery (#26): request a one-time email link, then set a new password with
/// it. All endpoints are anonymous — the user has no session. Deliberately a poor oracle: the request
/// endpoint always returns the same 202, and the token endpoints collapse unknown/expired/used into
/// one opaque 410. A reset is offered only to a <c>EmailVerified</c> account with mail configured;
/// everything else recovers through an admin (§6). See docs/feature-26-password-reset.md §5/§7.
/// </summary>
[ApiController]
[Route("api/auth")]
public class PasswordResetController(
    AppDbContext db,
    PasswordResetService resets,
    EmailSettingsService emailSettings,
    CredentialValidator credentials,
    SessionService sessions,
    SessionCookie cookie,
    TimeProvider clock,
    ILogger<PasswordResetController> log) : ControllerBase
{
    /// <summary>Whether this deployment can send self-service reset links (mail is configured).</summary>
    [HttpGet("capabilities")]
    [ProducesResponseType<ResetCapabilities>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ResetCapabilities>> Capabilities(CancellationToken ct) =>
        new ResetCapabilities(await emailSettings.IsEnabledAsync(ct));

    /// <summary>
    /// Requests a reset link. <b>Always</b> returns 202 with one fixed body — whether the account
    /// exists, is verified, is active, or mail is configured — so the endpoint reveals nothing about
    /// account state. A link is minted and sent only when all of those hold (§5.1). Rate-limited to
    /// blunt enumeration and outbound-mail abuse.
    /// </summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimiterPolicies.ForgotPassword)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Forgot(ForgotPasswordRequest req, CancellationToken ct)
    {
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();

        // Look the account up regardless of the branch we'll take, so the work is roughly uniform
        // whether or not the address exists.
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        // Send only to an active (claimed), verified account, and only when mail can actually go out.
        // A pending account (null hash) has no password to reset — it uses resend-invite instead.
        var eligible = user is { PasswordHash: not null, EmailVerified: true, DeletionRequestedAt: null }
                       && await emailSettings.IsEnabledAsync(ct);

        if (eligible)
        {
            try
            {
                // One live token per account: supersede any prior link, then commit before sending
                // (a transport failure must not roll back — the neutral 202 already went out).
                await resets.RemoveExistingAsync(user!.Id, ct);
                var (token, raw) = resets.Build(user.Id);
                db.PasswordResetTokens.Add(token);
                await db.SaveChangesAsync(ct);

                await resets.SendAsync(user.Email, raw, ct);
            }
            catch (Exception ex)
            {
                // Non-fatal: never leak failure to the caller (that would be an oracle). The user can
                // request another link; a fresh request supersedes this token.
                log.LogError(ex, "Password-reset email for a requested account failed to send.");
            }
        }

        // The single neutral response, identical on every branch.
        return Accepted(new { message = "If an account with that email can be reset by email, we've sent a link." });
    }

    /// <summary>Validates a reset token and returns the account email to prime the form.</summary>
    [HttpGet("reset-password/{token}")]
    [ProducesResponseType<ResetPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public async Task<ActionResult<ResetPreview>> Preview(string token, CancellationToken ct)
    {
        var row = await resets.ResolveAsync(token, ct);
        if (row is null)
            return Problem("This reset link is no longer valid.", statusCode: StatusCodes.Status410Gone);

        return new ResetPreview(row.User.Email);
    }

    /// <summary>
    /// Completes the reset: sets the new password, spends the token, revokes <b>every</b> existing
    /// session (a reset means "I may have lost control — sign everyone out"), and signs the user in
    /// on this browser. The password is held to the registration rules. See §5.3.
    /// </summary>
    [HttpPost("reset-password/{token}")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Reset(string token, ResetPasswordRequest req, CancellationToken ct)
    {
        var row = await resets.ResolveAsync(token, ct);
        if (row is null)
            return Problem("This reset link is no longer valid.", statusCode: StatusCodes.Status410Gone);

        var user = row.User;

        if (await credentials.ValidatePasswordAsync(req.Password, user.Email, ct) is { } errors)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = string.Join(" ", errors.Values.SelectMany(v => v))
            });

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Single-winner: two concurrent completions (a retried submit) both resolved the token as
        // usable above; decide the winner by an atomic conditional update — only the request whose
        // UPDATE actually flips UsedAt proceeds. Otherwise both would set a password and get a
        // session, with the last write silently winning.
        var won = await db.PasswordResetTokens
            .Where(t => t.Id == row.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, clock.GetUtcNow()), ct);
        if (won == 0)
            return Problem("This reset link is no longer valid.", statusCode: StatusCodes.Status410Gone);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        user.MustChangePassword = false;
        // Completing an email reset proves control of the inbox — keep the verification invariant total.
        user.EmailVerified = true;

        // Boot every existing session, then issue one fresh session for this browser: the person who
        // just proved inbox control and set the password is in, everyone else is out.
        await sessions.RevokeAllForUserAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);

        var sessionToken = await sessions.IssueAsync(
            user,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct);
        await tx.CommitAsync(ct);

        cookie.Set(Response, sessionToken);
        return Ok(new SessionResponse(user.Email, user.Role.ToString()));
    }
}
