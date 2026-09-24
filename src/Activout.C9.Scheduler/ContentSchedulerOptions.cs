namespace Activout.C9.Scheduler;

/// <summary>
/// Root configuration for the content scheduler, normally bound from the <c>ContentScheduler</c>
/// configuration section.
/// </summary>
public sealed class ContentSchedulerOptions
{
    /// <summary>Contentful space ID.</summary>
    public string SpaceId { get; set; } = "";

    /// <summary>Contentful environment ID, e.g. <c>master</c>.</summary>
    public string Environment { get; set; } = "";

    /// <summary>
    /// Content Management API token. Must belong to an identity dedicated to the scheduler:
    /// Scheduled Actions created by this identity are considered owned by the scheduler and may be
    /// cancelled when no longer desired.
    /// </summary>
    public string ManagementToken { get; set; } = "";

    /// <summary>Cron expression (evaluated in UTC) controlling how often reconciliation runs.</summary>
    public string ReconcileCron { get; set; } = "";

    /// <summary>How many days ahead Scheduled Actions are maintained.</summary>
    public int LookAheadDays { get; set; } = 7;

    /// <summary>Lease time for the reconciliation lock.</summary>
    public TimeSpan LockLease { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maximum number of pending Scheduled Actions in the environment (owned and external).
    /// When reached, the earliest desired actions are created first and the rest are logged
    /// and deferred to later runs.
    /// </summary>
    public int MaxPendingActions { get; set; } = 500;

    /// <summary>The configured schedules.</summary>
    public IReadOnlyList<ContentSchedule> Schedules { get; set; } = [];
}
