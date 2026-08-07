using Keepr.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Keepr.Api.Features.Auth;

/// <summary>The address a pending email change will move the account to, so the confirm form can show
/// what it's confirming.</summary>
public record EmailChangePreview(string NewEmail);

/// <summary>The account's new email after a confirmed change.</summary>
public record EmailChangeResult(string Email);

/// <summary>
/// Confirms a pending change of an account's login email (#27). Both endpoints are anonymous and
/// token-authorized — the confirmation link may be opened in any browser, signed in or not, exactly
/// like the reset and claim links. The token is the authorization. Unknown / expired / used all
/// collapse to one opaque 410. Confirming is the only self-service path that proves control of the new
/// inbox, so it sets <c>EmailVerified</c>. See docs/feature-27-change-email.md §5.2/§5.3.
/// </summary>
[ApiController]
[Route("api/auth")]
public class EmailChangeController(
    AppDbContext db,
    EmailChangeService emailChanges,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<EmailChangeController> log) : ControllerBase
{
    /// <summary>Validates a pending-change token and returns the target address to prime the form.
    /// Side-effect-free by design: a link prefetcher must not confirm the change (§5.2).</summary>
    [HttpGet("confirm-email/{token}")]
    [ProducesResponseType<EmailChangePreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public async Task<ActionResult<EmailChangePreview>> Preview(string token, CancellationToken ct)
    {
        var row = await emailChanges.ResolveAsync(token, ct);
        if (row is null)
            return Problem("This confirmation link is no longer valid.", statusCode: StatusCodes.Status410Gone);

        return new EmailChangePreview(row.NewEmail);
    }

    /// <summary>
    /// Applies the pending change: swaps <c>Email</c> to the confirmed address, sets
    /// <c>EmailVerified = true</c> (clicking a link that reached the new inbox is the proof), spends the
    /// token, and emails the <b>old</b> address a heads-up. Sessions are untouched — no secret rotated.
    /// See §5.3.
    /// </summary>
    [HttpPost("confirm-email/{token}")]
    [ProducesResponseType<EmailChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Confirm(string token, CancellationToken ct)
    {
        var row = await emailChanges.ResolveAsync(token, ct);
        if (row is null)
            return Problem("This confirmation link is no longer valid.", statusCode: StatusCodes.Status410Gone);

        var user = row.User;
        var oldEmail = user.Email;

        // Re-check uniqueness: another account may have taken NewEmail since the request was made
        // (unlikely under admin-only registration, but the window is real). Refuse before consuming.
        if (await db.Users.AnyAsync(u => u.Email == row.NewEmail && u.Id != user.Id, ct))
            return EmailInUse();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Single-winner: two concurrent confirms of the same token both resolved it as usable above;
        // only the request whose UPDATE flips UsedAt proceeds (guards a double-submit), the same guard
        // the reset/claim flows use.
        var won = await db.EmailChangeTokens
            .Where(t => t.Id == row.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, clock.GetUtcNow()), ct);
        if (won == 0)
            return Problem("This confirmation link is no longer valid.", statusCode: StatusCodes.Status410Gone);

        user.Email = row.NewEmail;
        // Confirming proves control of the new inbox — the only self-service path that verifies (§7).
        user.EmailVerified = true;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent registration/change took NewEmail between the pre-check and here — the unique
            // index on Users.Email rejects it. The token stays unspent (the transaction rolls back), so
            // the user can cancel and retry another address.
            return EmailInUse();
        }

        await tx.CommitAsync(ct);

        // Tell the OLD address the change happened, off the request's critical path. The change already
        // committed, so a failed notice is logged, never surfaced or rolled back.
        DispatchChangedNotice(oldEmail, user.Email);

        return Ok(new EmailChangeResult(user.Email));
    }

    /// <summary>Sends the old-address heads-up on a background task with its own DI scope (its own
    /// DbContext / sender) and lifetime — never the request's <c>CancellationToken</c>, which ends when
    /// the response returns. Fire-and-forget: a failed send is non-fatal and only logged.</summary>
    private void DispatchChangedNotice(string oldEmail, string newEmail)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<EmailChangeService>();
                await svc.SendChangedNoticeAsync(oldEmail, newEmail, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Background email-changed notice to the old address failed.");
            }
        });
    }

    private ConflictObjectResult EmailInUse()
    {
        var pd = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Detail = "That email is already in use."
        };
        pd.Extensions["code"] = "email_in_use";
        return Conflict(pd);
    }
}
