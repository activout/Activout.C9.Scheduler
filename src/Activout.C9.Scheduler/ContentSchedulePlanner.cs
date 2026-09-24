using Cronos;

namespace Activout.C9.Scheduler;

internal static class ContentSchedulePlanner
{
    /// <summary>
    /// All publish/unpublish occurrences of <paramref name="schedule"/> within [from, to], as UTC
    /// instants. Cron expressions are evaluated in the schedule's time zone, so DST is handled by Cronos.
    /// </summary>
    public static IEnumerable<(string Action, DateTimeOffset At)> Occurrences(
        ContentSchedule schedule, DateTimeOffset from, DateTimeOffset to)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        foreach (var (action, cron) in new[] { (ScheduledActionTypes.Publish, schedule.Publish), (ScheduledActionTypes.Unpublish, schedule.Unpublish) })
        {
            if (string.IsNullOrWhiteSpace(cron)) continue;
            foreach (var at in CronExpression.Parse(cron).GetOccurrences(from, to, zone, fromInclusive: true, toInclusive: true))
                yield return (action, at.ToUniversalTime());
        }
    }
}
