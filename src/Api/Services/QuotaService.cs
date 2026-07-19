using Keepr.Api.Data;
using Keepr.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Services;

/// <summary>
/// Quota accounting: reserve-on-start, reconcile-on-complete, release-on-abort/delete.
/// All mutations take a row lock on the user to stop two parallel uploads both "fitting".
/// See docs/ai-design-decisions.md (D9).
/// </summary>
public class QuotaService(AppDbContext db)
{
    /// <summary>
    /// Reserve <paramref name="declaredBytes"/> against the user's quota. Throws
    /// <see cref="QuotaExceededException"/> if it would exceed. Caller owns the surrounding
    /// transaction so the reserve and the MediaFile insert commit together.
    /// </summary>
    public async Task ReserveAsync(Guid userId, long declaredBytes, CancellationToken ct = default)
    {
        var user = await LockUserAsync(userId, ct);
        if (user.UsedBytes + declaredBytes > user.QuotaBytes)
            throw new QuotaExceededException(declaredBytes, user.RemainingBytes);

        user.UsedBytes += declaredBytes;
    }

    /// <summary>Adjust usage by (actual − declared) once the true size is known.</summary>
    public async Task ReconcileAsync(Guid userId, long declaredBytes, long actualBytes, CancellationToken ct = default)
    {
        var user = await LockUserAsync(userId, ct);
        user.UsedBytes += actualBytes - declaredBytes;
        if (user.UsedBytes < 0) user.UsedBytes = 0;
    }

    /// <summary>Give bytes back (abort or delete).</summary>
    public async Task ReleaseAsync(Guid userId, long bytes, CancellationToken ct = default)
    {
        var user = await LockUserAsync(userId, ct);
        user.UsedBytes = Math.Max(0, user.UsedBytes - bytes);
    }

    // Postgres row lock so concurrent uploads serialize on the user row.
    // The table is schema-qualified because raw SQL (unlike EF's generated queries) does not pick
    // up the model's default schema. {0} is a parameter; the schema name is a trusted constant.
    private async Task<User> LockUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users
            .FromSqlRaw($"SELECT * FROM {AppDbContext.Schema}.\"Users\" WHERE \"Id\" = {{0}} FOR UPDATE", userId)
            .SingleAsync(ct);
        return user;
    }
}
