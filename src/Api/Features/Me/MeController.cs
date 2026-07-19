using Keepr.Api.Data;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Features.Me;

public record UsageResponse(long QuotaBytes, long UsedBytes, long RemainingBytes);

[ApiController]
[Authorize]
[Route("api/me")]
public class MeController(AppDbContext db) : ControllerBase
{
    /// <summary>Powers the always-visible "space remaining" meter.</summary>
    [HttpGet("usage")]
    public async Task<ActionResult<UsageResponse>> Usage(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.UserId()], ct);
        if (user is null) return NotFound();
        return new UsageResponse(user.QuotaBytes, user.UsedBytes, user.RemainingBytes);
    }
}
