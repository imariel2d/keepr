using Keepr.Api.Data;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Features.Me;

/// <summary>
/// <see cref="TrashedBytes"/> is the part of <see cref="UsedBytes"/> held by trashed files.
/// Trash still counts against quota until it is purged — surfacing it lets the UI answer the
/// inevitable "I deleted everything and I'm still full".
/// </summary>
public record UsageResponse(long QuotaBytes, long UsedBytes, long RemainingBytes, long TrashedBytes);

[ApiController]
[Authorize]
[Route("api/me")]
public class MeController(AppDbContext db, TrashService trash) : ControllerBase
{
    /// <summary>Powers the always-visible "space remaining" meter.</summary>
    [HttpGet("usage")]
    public async Task<ActionResult<UsageResponse>> Usage(CancellationToken ct)
    {
        var userId = User.UserId();
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return NotFound();

        var trashed = await trash.TrashedBytesAsync(userId, ct);
        return new UsageResponse(user.QuotaBytes, user.UsedBytes, user.RemainingBytes, trashed);
    }
}
