using Cronos;
using Microsoft.Extensions.Options;

namespace Activout.C9.Scheduler;

internal sealed class ContentSchedulerOptionsValidator : IValidateOptions<ContentSchedulerOptions>
{
    public ValidateOptionsResult Validate(string? name, ContentSchedulerOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SpaceId)) errors.Add("SpaceId is required.");
        if (string.IsNullOrWhiteSpace(options.Environment)) errors.Add("Environment is required.");
        if (string.IsNullOrWhiteSpace(options.ManagementToken)) errors.Add("ManagementToken is required.");
        ValidateCron(options.ReconcileCron, "ReconcileCron", required: true, errors);
        if (options.LookAheadDays <= 0) errors.Add("LookAheadDays must be greater than zero.");
        if (options.LockLease <= TimeSpan.Zero) errors.Add("LockLease must be greater than zero.");
        if (options.MaxPendingActions <= 0) errors.Add("MaxPendingActions must be greater than zero.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Schedules.Count; i++)
        {
            var schedule = options.Schedules[i];
            var label = string.IsNullOrWhiteSpace(schedule.Name) ? $"Schedules[{i}]" : $"Schedule '{schedule.Name}'";

            if (string.IsNullOrWhiteSpace(schedule.Name)) errors.Add($"{label}: Name is required.");
            else if (!names.Add(schedule.Name)) errors.Add($"{label}: duplicate schedule name.");

            if (schedule.Selector is null || schedule.Selector.IsEmpty)
                errors.Add($"{label}: Selector must set at least one of Tag, ContentType or EntryId.");

            if (string.IsNullOrWhiteSpace(schedule.Publish) && string.IsNullOrWhiteSpace(schedule.Unpublish))
                errors.Add($"{label}: at least one of Publish or Unpublish is required.");
            ValidateCron(schedule.Publish, $"{label}: Publish", required: false, errors);
            ValidateCron(schedule.Unpublish, $"{label}: Unpublish", required: false, errors);

            // Contentful expects IANA IDs, so Windows IDs are rejected even where the OS knows them.
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(schedule.TimeZone ?? "", out var zone) || !zone.HasIanaId)
                errors.Add($"{label}: TimeZone '{schedule.TimeZone}' is not a known IANA time zone.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateCron(string? expression, string label, bool required, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            if (required) errors.Add($"{label} is required.");
            return;
        }

        try
        {
            CronExpression.Parse(expression);
        }
        catch (CronFormatException ex)
        {
            errors.Add($"{label}: invalid cron expression '{expression}': {ex.Message}");
        }
    }
}
