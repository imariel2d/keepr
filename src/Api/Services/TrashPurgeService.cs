using Keepr.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Services;

/// <summary>
/// Permanently deletes trashed items once the retention window expires — the only place bytes
/// actually leave R2 and quota is freed. Deliberately a background job: it is the one step that
/// spans two systems without a shared transaction, so it retries on the next tick instead of
/// failing in a user's request. See docs/trash-soft-delete-design.md (§4.4).
///
/// Single-instance safe, same caveat as <see cref="UploadCleanupService"/>: two instances
/// sweeping at once would double-release quota. Add an advisory lock before scaling out.
/// </summary>
public class TrashPurgeService(
    IServiceProvider services,
    IOptions<CleanupOptions> options,
    ILogger<TrashPurgeService> log) : BackgroundService
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
                log.LogError(ex, "Trash purge sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trash = scope.ServiceProvider.GetRequiredService<TrashService>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(0, _opt.TrashRetentionDays));

        // Files first, in batches. Oldest first so that a folder trashed after one of its own
        // subfolders is purged after it, keeping the parent FK satisfiable.
        var purged = 0;
        while (true)
        {
            var batch = await db.MediaFiles.IgnoreQueryFilters()
                .Where(m => m.DeletedAt != null && m.DeletedAt < cutoff)
                .OrderBy(m => m.DeletedAt)
                .Take(Math.Max(1, _opt.PurgeBatchSize))
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            await trash.PurgeFilesAsync(batch, ct);
            purged += batch.Count;

            if (ct.IsCancellationRequested) return;
        }

        // Then the folder rows they were sitting in. PurgeFolderRowsAsync deletes only folders
        // with no remaining children, repeatedly, so the Restrict FK is never violated.
        var folderIds = await db.Folders.IgnoreQueryFilters()
            .Where(f => f.DeletedAt != null && f.DeletedAt < cutoff)
            .Select(f => f.Id)
            .ToListAsync(ct);

        await trash.PurgeFolderRowsAsync(folderIds, ct);

        if (purged > 0 || folderIds.Count > 0)
            log.LogInformation(
                "Trash purge: removed {Files} file(s) and up to {Folders} folder(s) past the {Days}-day window.",
                purged, folderIds.Count, _opt.TrashRetentionDays);
    }
}
