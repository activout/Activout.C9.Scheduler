namespace Activout.C9.Scheduler;

internal static class ScheduledActionTypes
{
    public const string Publish = "publish";
    public const string Unpublish = "unpublish";
}

/// <summary>A pending Contentful Scheduled Action, reduced to what reconciliation needs.</summary>
internal sealed record ScheduledAction(
    string Id,
    int Version,
    string Action,
    string EntityType,
    string EntityId,
    DateTimeOffset ScheduledFor,
    string? TimeZone,
    string? CreatedById);

internal sealed record ScheduledActionRequest(
    string Action,
    string EntryId,
    DateTimeOffset ScheduledFor,
    string TimeZone);
