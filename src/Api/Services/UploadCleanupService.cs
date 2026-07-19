using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Services;

public class CleanupOptions
{
    public const string SectionName = "Cleanup";
    public int IntervalMinutes { get; set; } = 30;
    public int PendingMaxAgeHours { get; set; } = 24;
}

/// <summary>
/// Sweeps abandoned multipart uploads: a client that starts an upload but never completes it
/// leaves R2 parts AND reserved quota dangling. Periodically aborts such uploads and frees the
/// quota. See docs/ai-design-decisions.md (D-cleanup).
///
/// Single-instance safe. If the app is ever scaled out, move this to a dedicated worker or add
/// a lease/lock so only one instance sweeps at a time.
/// </summary>
public class UploadCleanupService(
    IServiceProvider services,
    IOptions<CleanupOptions> options,
    ILogger<UploadCleanupService> log) : BackgroundService
{
    private readonly CleanupOptions _opt = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _opt.IntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        // Run once at startup, then on each tick.
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Upload cleanup sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var quota = scope.ServiceProvider.GetRequiredService<QuotaService>();

        var cutoff = DateTimeOffset.UtcNow.AddHours(-_opt.PendingMaxAgeHours);

        var stale = await db.MediaFiles
            .Where(m => m.Status == MediaStatus.Pending && m.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0) return;
        log.LogInformation("Upload cleanup: found {Count} abandoned upload(s).", stale.Count);

        foreach (var media in stale)
        {
            // Best-effort abort in storage; proceed to free quota even if it's already gone.
            if (media.MultipartUploadId is not null)
            {
                try
                {
                    await storage.AbortMultipartUploadAsync(media.StorageKey, media.MultipartUploadId, ct);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Could not abort multipart upload for {Key}.", media.StorageKey);
                }
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await quota.ReleaseAsync(media.OwnerId, media.SizeBytes, ct);
            media.Status = MediaStatus.Failed;
            media.MultipartUploadId = null;
            media.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
