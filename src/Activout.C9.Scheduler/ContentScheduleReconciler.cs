using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Activout.C9.Scheduler;

/// <summary>Outcome of one reconciliation run.</summary>
/// <param name="Desired">Scheduled Actions wanted by the configuration within the window.</param>
/// <param name="Pending">Pending Scheduled Actions in the environment (owned and external).</param>
/// <param name="Owned">Pending actions owned by the scheduler identity within the window.</param>
/// <param name="Unchanged">Owned actions already matching a desired action.</param>
/// <param name="Created">Actions created (or, in a dry run, that would be created).</param>
/// <param name="Updated">Actions updated (or that would be updated).</param>
/// <param name="Cancelled">Obsolete owned actions cancelled (or that would be cancelled).</param>
/// <param name="Failed">Individual CMA operations that failed.</param>
/// <param name="DeferredByLimit">Desired actions not created because of <see cref="ContentSchedulerOptions.MaxPendingActions"/>.</param>
/// <param name="ScheduleFailures">Schedules whose entries could not be resolved; no cancellations are made when non-zero.</param>
public sealed record ReconciliationResult(
    int Desired, int Pending, int Owned, int Unchanged, int Created, int Updated, int Cancelled, int Failed, int DeferredByLimit,
    int ScheduleFailures);

/// <summary>
/// Desired-state reconciliation: computes every Scheduled Action the configuration wants within the
/// look-ahead window and makes the owned actions in Contentful match. Holds no state between runs.
/// Normally driven by the hosted worker; can also be resolved from DI and run once directly.
/// </summary>
public sealed class ContentScheduleReconciler
{
    private readonly IContentClient contentClient;
    private readonly IScheduledActionsClient scheduledActions;
    private readonly IOptions<ContentSchedulerOptions> options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ContentScheduleReconciler> logger;

    internal ContentScheduleReconciler(
        IContentClient contentClient,
        IScheduledActionsClient scheduledActions,
        IOptions<ContentSchedulerOptions> options,
        TimeProvider timeProvider,
        ILogger<ContentScheduleReconciler> logger)
    {
        this.contentClient = contentClient;
        this.scheduledActions = scheduledActions;
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Occurrences closer than this are neither created nor touched; Contentful rejects near-past times.</summary>
    internal static readonly TimeSpan MinimumLeadTime = TimeSpan.FromMinutes(1);

    private readonly record struct ActionKey(string EntryId, string Action, DateTimeOffset At);

    /// <summary>Runs one reconciliation.</summary>
    /// <param name="dryRun">When true, log the planned changes without modifying Contentful.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public async Task<ReconciliationResult> Reconcile(bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        var now = timeProvider.GetUtcNow();
        var from = now + MinimumLeadTime;
        var to = now.AddDays(o.LookAheadDays);

        logger.LogInformation("Content schedule reconciliation started for {SpaceId}/{Environment}, window {From:o} to {To:o}",
            o.SpaceId, o.Environment, from, to);

        // 1. Desired state. Value = time zone to store on the action.
        var desired = new Dictionary<ActionKey, string>();
        var scheduleFailures = 0;
        foreach (var schedule in o.Schedules)
        {
            try
            {
                var entryIds = await contentClient.ResolveEntryIds(schedule.Selector, cancellationToken);
                var occurrences = ContentSchedulePlanner.Occurrences(schedule, from, to).ToList();
                foreach (var entryId in entryIds)
                foreach (var (action, at) in occurrences)
                    desired.TryAdd(new ActionKey(entryId, action, Normalize(at)), schedule.TimeZone);

                logger.LogInformation("Schedule {Schedule} matched {EntryCount} entries with {OccurrenceCount} occurrences",
                    schedule.Name, entryIds.Count, occurrences.Count);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                scheduleFailures++;
                logger.LogError(ex, "Schedule {Schedule} failed to resolve entries ({Selector}); its actions are left unchanged this run",
                    schedule.Name, schedule.Selector);
            }
        }

        // 2. Actual state, split by ownership.
        var userId = await contentClient.GetCurrentUserId(cancellationToken);
        var pending = await scheduledActions.GetPending(cancellationToken);
        var owned = pending
            .Where(a => a.CreatedById == userId && a.EntityType == "Entry" && a.ScheduledFor >= from)
            .ToList();

        // 3. Diff. Owned actions are the only ones ever mutated; external ones only count toward the limit.
        var remaining = new Dictionary<ActionKey, string>(desired);
        var updates = new List<(ScheduledAction Action, string TimeZone)>();
        var cancels = new List<ScheduledAction>();
        var unchanged = 0;
        foreach (var action in owned.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            var key = new ActionKey(action.EntityId, action.Action, Normalize(action.ScheduledFor));
            if (remaining.Remove(key, out var timeZone))
            {
                if (action.TimeZone == timeZone) unchanged++;
                else updates.Add((action, timeZone));
            }
            else
            {
                cancels.Add(action); // obsolete, or a duplicate of an already matched action
            }
        }

        if (scheduleFailures > 0 && cancels.Count > 0)
        {
            logger.LogWarning("Skipping {CancelCount} cancellations because at least one schedule failed to resolve", cancels.Count);
            cancels.Clear();
        }

        var creates = remaining.OrderBy(kv => kv.Key.At).ThenBy(kv => kv.Key.EntryId, StringComparer.Ordinal).ToList();
        var room = Math.Max(0, o.MaxPendingActions - (pending.Count - cancels.Count));
        var deferred = Math.Max(0, creates.Count - room);
        if (deferred > 0)
        {
            logger.LogWarning(
                "{DesiredCount} actions to create but only room for {Room} under MaxPendingActions={Max}; deferring the {Deferred} latest to later runs",
                creates.Count, room, o.MaxPendingActions, deferred);
            creates = creates.Take(room).ToList();
        }

        logger.LogInformation(
            "Desired {Desired} actions; {Pending} pending in Contentful of which {Owned} owned; {Unchanged} unchanged, {Create} to create, {Update} to update, {Cancel} to cancel",
            desired.Count, pending.Count, owned.Count, unchanged, creates.Count, updates.Count, cancels.Count);

        if (dryRun)
        {
            foreach (var a in cancels) logger.LogInformation("[dry run] would cancel {Action} of entry {EntryId} at {At} ({Id})", a.Action, a.EntityId, FormatTime(a.ScheduledFor, a.TimeZone), a.Id);
            foreach (var (a, tz) in updates) logger.LogInformation("[dry run] would update {Action} of entry {EntryId} at {At} to time zone {TimeZone} ({Id})", a.Action, a.EntityId, FormatTime(a.ScheduledFor, a.TimeZone), tz, a.Id);
            foreach (var (k, tz) in creates) logger.LogInformation("[dry run] would create {Action} of entry {EntryId} at {At}", k.Action, k.EntryId, FormatTime(k.At, tz));
            return new ReconciliationResult(desired.Count, pending.Count, owned.Count, unchanged, creates.Count, updates.Count, cancels.Count, 0, deferred, scheduleFailures);
        }

        // 4. Apply sequentially: cancel first to free capacity. Individual failures don't stop the run.
        int cancelled = 0, updated = 0, created = 0, failed = 0;
        foreach (var action in cancels)
        {
            if (await Try(() => scheduledActions.Cancel(action.Id, cancellationToken), "cancel", action.EntityId, action.Action, FormatTime(action.ScheduledFor, action.TimeZone), cancellationToken))
                cancelled++;
            else failed++;
        }

        foreach (var (action, timeZone) in updates)
        {
            var request = new ScheduledActionRequest(action.Action, action.EntityId, action.ScheduledFor, timeZone);
            if (await Try(() => scheduledActions.Update(action.Id, action.Version, request, cancellationToken), "update", action.EntityId, action.Action, FormatTime(action.ScheduledFor, timeZone), cancellationToken))
                updated++;
            else failed++;
        }

        foreach (var (key, timeZone) in creates)
        {
            var request = new ScheduledActionRequest(key.Action, key.EntryId, key.At, timeZone);
            if (await Try(() => scheduledActions.Create(request, cancellationToken), "create", key.EntryId, key.Action, FormatTime(key.At, timeZone), cancellationToken))
                created++;
            else failed++;
        }

        var result = new ReconciliationResult(desired.Count, pending.Count, owned.Count, unchanged, created, updated, cancelled, failed, deferred, scheduleFailures);
        logger.Log(failed > 0 ? LogLevel.Warning : LogLevel.Information,
            "Content schedule reconciliation completed: {Created} created, {Updated} updated, {Cancelled} cancelled, {Failed} failed",
            created, updated, cancelled, failed);
        return result;
    }

    private async Task<bool> Try(Func<Task> operation, string verb, string entryId, string action, string at, CancellationToken cancellationToken)
    {
        try
        {
            await operation();
            logger.LogDebug("{Verb} {Action} of entry {EntryId} at {At}", verb, action, entryId, at);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to {Verb} {Action} of entry {EntryId} at {At}", verb, action, entryId, at);
            return false;
        }
    }

    /// <summary>
    /// Local time in the action's time zone followed by UTC, e.g.
    /// <c>2026-09-26 23:30 Europe/Stockholm (2026-09-26T21:30:00Z)</c>, so logs read like the cron config.
    /// </summary>
    internal static string FormatTime(DateTimeOffset at, string? timeZone)
    {
        var utc = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(timeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)) return utc;
        var local = TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return $"{local} {timeZone} ({utc})";
    }

    /// <summary>UTC, truncated to whole seconds, so round-tripped Contentful timestamps compare equal.</summary>
    internal static DateTimeOffset Normalize(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
}
