using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Keepr.Api.Services;

/// <summary>
/// Recognises the unique-index violation raised when two requests race for the same name.
/// <see cref="NameAllocator"/> probes for a free name, but the probe and the insert are not
/// atomic — the index is the real referee, so callers retry when it says no.
/// </summary>
public static class NameConflict
{
    private const string UniqueViolation = "23505";

    /// <summary>True when the failure is a name collision, not some other constraint.</summary>
    public static bool IsNameCollision(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation } pg
        && pg.ConstraintName?.Contains("NameLower", StringComparison.Ordinal) == true;

    /// <summary>
    /// Runs <paramref name="operation"/>, retrying when a racing writer takes the name first.
    /// The context is cleared between attempts so the retry re-reads what is actually taken.
    /// </summary>
    public static async Task<T> RetryAsync<T>(
        DbContext db, Func<Task<T>> operation, int attempts = 5)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (DbUpdateException ex) when (attempt < attempts && IsNameCollision(ex))
            {
                // Drop the failed insert/update; the next attempt re-probes for a free name.
                db.ChangeTracker.Clear();
            }
        }
    }
}
