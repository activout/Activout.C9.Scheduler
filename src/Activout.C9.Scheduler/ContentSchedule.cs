namespace Activout.C9.Scheduler;

/// <summary>
/// A named rule that schedules publish and/or unpublish actions, using cron expressions evaluated
/// in <see cref="TimeZone"/>, for every entry matched by <see cref="Selector"/>.
/// </summary>
public sealed class ContentSchedule
{
    /// <summary>Unique name, used for logging.</summary>
    public string Name { get; set; } = "";

    /// <summary>Which entries this schedule applies to.</summary>
    public ContentSelector Selector { get; set; } = new();

    /// <summary>IANA time zone ID the cron expressions are evaluated in, e.g. <c>Europe/Stockholm</c>.</summary>
    public string TimeZone { get; set; } = "";

    /// <summary>Cron expression for publish actions, or <c>null</c> for none.</summary>
    public string? Publish { get; set; }

    /// <summary>Cron expression for unpublish actions, or <c>null</c> for none.</summary>
    public string? Unpublish { get; set; }
}
