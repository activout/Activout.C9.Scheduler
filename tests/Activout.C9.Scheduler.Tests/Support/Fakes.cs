using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Activout.C9.Scheduler.Tests.Support;

internal sealed class FakeContentClient : IContentClient
{
    public const string SchedulerUserId = "scheduler-user";

    public Dictionary<ContentSelector, IReadOnlyList<string>> Entries { get; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<ContentSelector> Failing { get; } = new(ReferenceEqualityComparer.Instance);
    public int Calls { get; private set; }
    public bool IdentityFails { get; set; }

    public Task<string> GetCurrentUserId(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        if (IdentityFails) throw new HttpRequestException("401 Unauthorized");
        return Task.FromResult(SchedulerUserId);
    }

    public Task<IReadOnlyList<string>> ResolveEntryIds(ContentSelector selector, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        if (Failing.Contains(selector)) throw new InvalidOperationException("CMA down");
        return Task.FromResult(Entries.GetValueOrDefault(selector) ?? []);
    }
}

/// <summary>In-memory Contentful: pending actions survive across reconciler instances, like the real thing.</summary>
internal sealed class FakeScheduledActionsClient : IScheduledActionsClient
{
    private int nextId;

    public List<ScheduledAction> Pending { get; } = [];
    public List<string> Operations { get; } = [];

    public ScheduledAction Add(string entryId, string action, DateTimeOffset at, string? createdBy = FakeContentClient.SchedulerUserId,
        string? timeZone = "Europe/Stockholm")
    {
        var a = new ScheduledAction($"sa{++nextId}", 1, action, "Entry", entryId, at, timeZone, createdBy);
        Pending.Add(a);
        return a;
    }

    public Task<IReadOnlyList<ScheduledAction>> GetPending(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScheduledAction>>(Pending.ToList());

    public Task<ScheduledAction> Create(ScheduledActionRequest request, CancellationToken cancellationToken)
    {
        Operations.Add($"create {request.Action} {request.EntryId} {request.ScheduledFor:o}");
        return Task.FromResult(Add(request.EntryId, request.Action, request.ScheduledFor, timeZone: request.TimeZone));
    }

    public Task<ScheduledAction> Update(string id, int version, ScheduledActionRequest request, CancellationToken cancellationToken)
    {
        Operations.Add($"update {id}");
        var index = Pending.FindIndex(a => a.Id == id);
        Pending[index] = Pending[index] with { ScheduledFor = request.ScheduledFor, TimeZone = request.TimeZone, Version = version + 1 };
        return Task.FromResult(Pending[index]);
    }

    public Task Cancel(string id, CancellationToken cancellationToken)
    {
        Operations.Add($"cancel {id}");
        Pending.RemoveAll(a => a.Id == id);
        return Task.CompletedTask;
    }
}

internal sealed class TestContext
{
    public static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    public FakeContentClient Content { get; } = new();
    public FakeScheduledActionsClient Actions { get; } = new();
    public FakeTimeProvider Time { get; } = new(Now);
    public ContentSchedulerOptions Options { get; } = new()
    {
        SpaceId = "space",
        Environment = "master",
        ManagementToken = "token",
        ReconcileCron = "*/10 * * * *",
        LookAheadDays = 1,
    };

    /// <summary>Adds a schedule matching <paramref name="entryIds"/>.</summary>
    public ContentSchedule AddSchedule(string name, string? publish, string? unpublish, params string[] entryIds)
    {
        var schedule = new ContentSchedule
        {
            Name = name,
            Selector = new ContentSelector { Tag = name },
            TimeZone = "Europe/Stockholm",
            Publish = publish,
            Unpublish = unpublish,
        };
        Options.Schedules = [.. Options.Schedules, schedule];
        Content.Entries[schedule.Selector] = entryIds;
        return schedule;
    }

    /// <summary>A fresh reconciler each call, mimicking an application restart.</summary>
    public ContentScheduleReconciler CreateReconciler() =>
        new(Content, Actions, Microsoft.Extensions.Options.Options.Create(Options), Time, NullLogger<ContentScheduleReconciler>.Instance);

    public Task<ReconciliationResult> Reconcile(bool dryRun = false) => CreateReconciler().Reconcile(dryRun);
}
