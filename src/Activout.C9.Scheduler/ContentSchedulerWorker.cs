using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Activout.C9.Scheduler;

/// <summary>
/// Runs reconciliation once at startup and then on every <see cref="ContentSchedulerOptions.ReconcileCron"/>
/// occurrence (UTC). A run is skipped, never queued, when the lock is held elsewhere.
/// </summary>
internal sealed class ContentSchedulerWorker(
    ContentScheduleReconciler reconciler,
    IContentSchedulerLock schedulerLock,
    IOptions<ContentSchedulerOptions> options,
    TimeProvider timeProvider,
    ILogger<ContentSchedulerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var cron = CronExpression.Parse(options.Value.ReconcileCron);
        await RunOnce(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var next = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);
            if (next is null)
            {
                logger.LogWarning("ReconcileCron {Cron} has no further occurrences; stopping", options.Value.ReconcileCron);
                return;
            }

            await Task.Delay(next.Value - now, timeProvider, stoppingToken);
            await RunOnce(stoppingToken);
        }
    }

    internal static string LockName(ContentSchedulerOptions o) => $"content-scheduler:{o.SpaceId}:{o.Environment}";

    internal async Task RunOnce(CancellationToken cancellationToken)
    {
        var o = options.Value;
        try
        {
            await using var handle = await schedulerLock.TryAcquire(LockName(o), o.LockLease, cancellationToken);
            if (handle is null)
            {
                logger.LogInformation("Content schedule reconciliation skipped: lock held by another instance");
                return;
            }

            var started = timeProvider.GetTimestamp();
            await reconciler.Reconcile(cancellationToken: cancellationToken);
            var elapsed = timeProvider.GetElapsedTime(started);
            if (elapsed > o.LockLease)
                logger.LogWarning("Reconciliation took {Elapsed}, longer than LockLease {Lease}; another instance may have run concurrently",
                    elapsed, o.LockLease);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Content schedule reconciliation failed; retrying at next occurrence");
        }
    }
}
