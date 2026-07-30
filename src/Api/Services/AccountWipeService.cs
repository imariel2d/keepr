using Keepr.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Services;

/// <summary>
/// Phase 2 of a kick: hard-deletes every file owned by an account an admin marked for deletion
/// (<c>User.DeletionRequestedAt</c>), then removes the account. Unlike <see cref="TrashPurgeService"/>
/// this ignores the retention clock and the trash/live distinction entirely — a kicked user's
/// files all go, immediately and unrecoverably. See docs/feature-34-admin-console.md §4.2.
///
/// A background job for the same reason purge is: it is the step that spans the database and R2
/// without a shared transaction, so it retries on the next tick instead of failing in the admin's
/// request. Phase 1 already revoked access, so being eventually-consistent here is safe.
///
/// Single-instance safe, same caveat as <see cref="TrashPurgeService"/>: two instances (or that
/// sweeper racing this one over the same user's trashed files) could double-release quota — which
/// <see cref="QuotaService.ReleaseAsync"/> clamps at zero, and the user is being deleted anyway.
/// Add an advisory lock before scaling out.
/// </summary>
public class AccountWipeService(
    IServiceProvider services,
    IOptions<CleanupOptions> options,
    ILogger<AccountWipeService> log) : BackgroundService
{
    private readonly CleanupOptions _opt = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _opt.IntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Account wipe sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trash = scope.ServiceProvider.GetRequiredService<TrashService>();

        var pending = await db.Users
            .Where(u => u.DeletionRequestedAt != null)
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var userId in pending)
        {
            if (ct.IsCancellationRequested) return;
            await WipeUserAsync(db, trash, userId, ct);
        }
    }

    private async Task WipeUserAsync(AppDbContext db, TrashService trash, Guid userId, CancellationToken ct)
    {
        // Every file the user owns — live, trashed, pending, failed — in batches so one enormous
        // account doesn't load into memory at once. IgnoreQueryFilters so trashed rows are included.
        var wiped = 0;
        while (true)
        {
            var batch = await db.MediaFiles.IgnoreQueryFilters()
                .Where(m => m.OwnerId == userId)
                .Take(Math.Max(1, _opt.PurgeBatchSize))
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            await trash.PurgeFilesAsync(batch, ct); // deletes objects from R2, releases quota, drops rows
            wiped += batch.Count;

            if (ct.IsCancellationRequested) return;
        }

        // Then the now-empty folder tree, deepest-first (PurgeFolderRowsAsync only deletes folders
        // with no remaining children or files, so the Restrict FK is never violated).
        var folderIds = await db.Folders.IgnoreQueryFilters()
            .Where(f => f.OwnerId == userId)
            .Select(f => f.Id)
            .ToListAsync(ct);
        await trash.PurgeFolderRowsAsync(folderIds, ct);

        // Finally the account row itself. Its sessions cascade at the database level; the audit
        // rows do not (they have no FK — they must outlive the account they describe).
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);

        log.LogInformation(
            "Account wipe: removed user {UserId} and {Files} file(s).", userId, wiped);
    }
}
