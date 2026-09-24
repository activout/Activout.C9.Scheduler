namespace Activout.C9.Scheduler;

/// <summary>Scheduled Actions CMA endpoints for the configured space and environment.</summary>
internal interface IScheduledActionsClient
{
    /// <summary>All actions with status <c>scheduled</c>, following pagination.</summary>
    Task<IReadOnlyList<ScheduledAction>> GetPending(CancellationToken cancellationToken);

    Task<ScheduledAction> Create(ScheduledActionRequest request, CancellationToken cancellationToken);

    /// <summary>Only <c>scheduledFor</c> changes are honoured by Contentful.</summary>
    Task<ScheduledAction> Update(string id, int version, ScheduledActionRequest request, CancellationToken cancellationToken);

    Task Cancel(string id, CancellationToken cancellationToken);
}
