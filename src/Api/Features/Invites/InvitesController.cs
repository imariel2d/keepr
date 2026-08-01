using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Features.Invites;

/// <summary>The invited address, so the claim form can show who the account is for.</summary>
public record InvitePreview(string Email);

/// <param name="Password">The password the invitee chooses; validated like a registration password.</param>
public record ClaimRequest(string Password);

/// <summary>
/// Public, token-gated claim of an admin-provisioned account (no session required — the invitee has
/// none yet). Mirrors the share-link viewer's shape. An unknown, expired, or already-claimed token
/// is one opaque <c>410 Gone</c>, so the endpoint reveals nothing about which. See
/// docs/feature-36-account-provisioning.md §8.4.
/// </summary>
[ApiController]
[Route("api/invites")]
public class InvitesController(
    AppDbContext db,
    InviteService invites,
    CredentialValidator credentials,
    SessionService sessions,
    SessionCookie cookie,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Validates a claim token and returns the invited email to prime the form.</summary>
    [HttpGet("{token}")]
    [ProducesResponseType<InvitePreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public async Task<ActionResult<InvitePreview>> Preview(string token, CancellationToken ct)
    {
        var invite = await invites.ResolveAsync(token, ct);
        if (invite is null)
            return Problem("This invitation is no longer valid.", statusCode: StatusCodes.Status410Gone);

        return new InvitePreview(invite.User.Email);
    }

    /// <summary>
    /// Claims the account: sets the chosen password, marks the invite spent, and signs the user in
    /// (issues a session cookie). The password is held to the same rules as registration.
    /// </summary>
    [HttpPost("{token}/claim")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Claim(string token, ClaimRequest req, CancellationToken ct)
    {
        var invite = await invites.ResolveAsync(token, ct);
        if (invite is null)
            return Problem("This invitation is no longer valid.", statusCode: StatusCodes.Status410Gone);

        var user = invite.User;

        if (await credentials.ValidatePasswordAsync(req.Password, user.Email, ct) is { } errors)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = string.Join(" ", errors.Values.SelectMany(v => v))
            });

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Single-winner: two concurrent claims (a retried submit) both resolved the invite as
        // claimable above, so decide the winner by an atomic conditional update — only the request
        // whose UPDATE actually flips ClaimedAt proceeds. Otherwise both would set a password and
        // get a session, with the last write silently winning. See §8.4.
        var won = await db.AccountInvites
            .Where(i => i.Id == invite.Id && i.ClaimedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ClaimedAt, clock.GetUtcNow()), ct);
        if (won == 0)
            return Problem("This invitation is no longer valid.", statusCode: StatusCodes.Status410Gone);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        // The invitee chose this password themselves, so there is nothing to force-rotate.
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);

        // Issue the session inside the transaction so it exists only if the whole claim commits.
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
