namespace Activout.C9.Scheduler.Tests;

public class PlannerTests
{
    private static List<(string Action, DateTimeOffset At)> Plan(string? publish, string? unpublish, string timeZone,
        DateTimeOffset from, DateTimeOffset to) =>
        ContentSchedulePlanner.Occurrences(
            new ContentSchedule { Name = "s", TimeZone = timeZone, Publish = publish, Unpublish = unpublish },
            from, to).ToList();

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void UsesScheduleTimeZone_NotHostTimeZone()
    {
        Assert.Equal([("publish", Utc(9, 24, 21, 30))], Plan("30 23 * * *", null, "Europe/Stockholm", Utc(9, 24, 0), Utc(9, 25, 0)));
        Assert.Equal([("publish", Utc(9, 24, 23, 30))], Plan("30 23 * * *", null, "UTC", Utc(9, 24, 0), Utc(9, 25, 0)));
    }

    [Fact]
    public void CombinedPublishAndUnpublish()
    {
        Assert.Equal(
            [("publish", Utc(9, 24, 21, 30)), ("unpublish", Utc(9, 24, 22))],
            Plan("30 23 * * *", "0 0 * * *", "Europe/Stockholm", Utc(9, 24, 12), Utc(9, 25, 12)));
    }

    [Fact]
    public void AutumnDstTransition_KeepsLocalTime()
    {
        // Stockholm leaves CEST on Sunday 2026-10-25.
        Assert.Equal(
            [Utc(10, 23, 21, 30), Utc(10, 24, 21, 30), Utc(10, 25, 22, 30), Utc(10, 26, 22, 30)],
            Plan("30 23 * * *", null, "Europe/Stockholm", Utc(10, 23, 0), Utc(10, 27, 0)).Select(o => o.At));
    }

    [Fact]
    public void SpringDstTransition_SkippedLocalTimeRunsOnceAtTransition()
    {
        // 02:30 does not exist in Stockholm on 2026-03-29 (02:00 -> 03:00 CEST).
        Assert.Equal(
            [Utc(3, 28, 1, 30), Utc(3, 29, 1, 0), Utc(3, 30, 0, 30)],
            Plan("30 2 * * *", null, "Europe/Stockholm", Utc(3, 28, 0), Utc(3, 31, 0)).Select(o => o.At));
    }

    [Fact]
    public void AutumnDstTransition_AmbiguousLocalTimeRunsOnce()
    {
        // 02:30 occurs twice in Stockholm on 2026-10-25.
        Assert.Single(Plan("30 2 25 10 *", null, "Europe/Stockholm", Utc(10, 24, 0), Utc(10, 26, 0)));
    }
}
