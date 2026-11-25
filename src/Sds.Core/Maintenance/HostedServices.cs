using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sds.Core.Computers;
using Sds.Core.Jobs;
using Sds.Core.Logging;
using Sds.Core.Multicast;
using Sds.Core.Storage;
using Sds.Data;

namespace Sds.Core.Maintenance;

/// <summary>
/// Runs once at startup, before the web server accepts requests: bootstrap of the share,
/// database migration, cleanup of interrupted jobs and a reconciliation pass.
/// </summary>
public sealed class StartupService(IServiceProvider services, ILogger<StartupService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Step 1: folders, share, groups.
        services.GetRequiredService<ShareBootstrapper>().Run();

        // Step 2: database schema (creates sds.db on first start) and WAL journal mode so
        // readers never block the writer.
        var dbFactory = services.GetRequiredService<IDbContextFactory<SdsDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            await db.Database.MigrateAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        }

        // Step 3: jobs that were running when the process stopped cannot resume.
        await services.GetRequiredService<JobService>().FailInterruptedJobsAsync(cancellationToken);

        // Step 4: temporary exports left behind by a stop that skipped the shutdown cleanup
        // (crash, killed process, power loss). Their jobs were just marked failed or are
        // from an earlier run, so nobody can still be downloading them.
        services.GetRequiredService<ExportCleaner>().ClearAll("startup");

        // Step 5: bring disk and database in line.
        await services.GetRequiredService<Reconciler>().RunAsync(cancellationToken);
        logger.LogInformation("SDS startup completed");
    }

    /// <summary>
    /// Deletes the temporary exports when the server shuts down.
    /// </summary>
    /// <remarks>
    /// Hosted services stop in the reverse order of registration. This service is registered
    /// first, so by the time this runs the web server has stopped serving downloads and the job
    /// runner has stopped taking jobs. An export that wimgapi is still writing cannot be deleted
    /// (the file is open); it is skipped here and removed by the startup step instead.
    /// </remarks>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        services.GetRequiredService<ExportCleaner>().ClearAll("shutdown");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Removes the temporary exports in Staging\exports. Exports written to a caller-chosen
/// DestinationPath are never touched: they are not temporary.
/// </summary>
public sealed class ExportCleaner(ShareLayout layout, ILogger<ExportCleaner> logger)
{
    /// <summary>Deletes every entry of the export staging folder.</summary>
    /// <param name="reason">Shown in the log line, for example "shutdown".</param>
    /// <returns>The number of entries deleted.</returns>
    public int ClearAll(string reason)
    {
        var dir = layout.ExportStaging;
        if (!Directory.Exists(dir))
        {
            return 0;
        }

        // Each export lives in its own Staging\exports\<guid> folder, so deleting the top level
        // entries removes whole exports at once.
        int deleted = 0, skipped = 0;
        foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            try
            {
                if (entry is DirectoryInfo d)
                {
                    d.Delete(recursive: true);
                }
                else
                {
                    entry.Delete();
                }

                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still open (an export being written or a download that did not end). The
                // next startup removes it.
                skipped++;
                logger.LogWarning("Could not delete temporary export {Path}: {Message}", entry.FullName, ex.Message);
            }
        }

        if (deleted > 0 || skipped > 0)
        {
            logger.LogInformation("Cleared {Deleted} temporary export(s) at {Reason}, {Skipped} skipped", deleted, reason, skipped);
        }

        return deleted;
    }
}

/// <summary>
/// Periodic housekeeping: multicast start criteria (every 15 seconds), pending device and
/// client log retention, and deletion of old staging files (every hour).
/// </summary>
public sealed class MaintenanceService(IServiceProvider services, IOptions<SdsOptions> options, ShareLayout layout, ILogger<MaintenanceService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var lastHourly = DateTime.MinValue;
        do
        {
            try
            {
                await services.GetRequiredService<MulticastService>().EvaluateAllAsync(stoppingToken);

                if (DateTime.UtcNow - lastHourly > TimeSpan.FromHours(1))
                {
                    lastHourly = DateTime.UtcNow;
                    await services.GetRequiredService<PendingDeviceService>().CleanupAsync(stoppingToken);
                    await services.GetRequiredService<ClientLogService>().CleanupAsync(stoppingToken);
                    CleanStaging();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Maintenance pass failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Deletes staging entries (uploads, exports) older than the retention period.</summary>
    private void CleanStaging()
    {
        var cutoff = DateTime.UtcNow.AddHours(-options.Value.StagingRetentionHours);
        foreach (var sub in new[] { "uploads", "exports", "copy", "drivers" })
        {
            var dir = Path.Combine(layout.Staging, sub);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if (entry.LastWriteTimeUtc >= cutoff)
                {
                    continue;
                }

                try
                {
                    if (entry is DirectoryInfo d)
                    {
                        d.Delete(recursive: true);
                    }
                    else
                    {
                        entry.Delete();
                    }
                }
                catch (IOException)
                {
                    // In use; next pass.
                }
            }
        }
    }
}
