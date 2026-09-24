using Activout.C9.Scheduler.Tests.Support;

namespace Activout.C9.Scheduler.Tests;

public class ReconcilerTests
{
    // Europe/Stockholm is UTC+2 on 2026-09-24. Window: 12:01Z on the 24th to 12:00Z on the 25th.
    private static readonly DateTimeOffset Publish2330 = new(2026, 9, 24, 21, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Unpublish0000 = new(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);

    private readonly TestContext ctx = new();

    [Fact]
    public async Task NothingExists_CreatesBoth()
    {
        ctx.AddSchedule("night", "30 23 * * *", "0 0 * * *", "e1");

        var result = await ctx.Reconcile();

        Assert.Equal(
            ["create publish e1 2026-09-24T21:30:00.0000000+00:00", "create unpublish e1 2026-09-24T22:00:00.0000000+00:00"],
            ctx.Actions.Operations);
        Assert.Equal(2, result.Created);
        Assert.All(ctx.Actions.Pending, a => Assert.Equal("Europe/Stockholm", a.TimeZone));
    }

    [Fact]
    public async Task OneExists_CreatesOnlyMissing()
    {
        ctx.AddSchedule("night", "30 23 * * *", "0 0 * * *", "e1");
        ctx.Actions.Add("e1", "publish", Publish2330);

        await ctx.Reconcile();

        Assert.Equal(["create unpublish e1 2026-09-24T22:00:00.0000000+00:00"], ctx.Actions.Operations);
    }

    [Fact]
    public async Task EverythingExists_CreatesNothing_AlsoAfterRestart()
    {
        ctx.AddSchedule("night", "30 23 * * *", "0 0 * * *", "e1");
        await ctx.Reconcile();
        ctx.Actions.Operations.Clear();

        var result = await ctx.Reconcile(); // new reconciler instance, no persisted state

        Assert.Empty(ctx.Actions.Operations);
        Assert.Equal(2, result.Unchanged);
    }

    [Fact]
    public async Task MultipleEntries_EachGetsEveryOccurrence()
    {
        ctx.Options.LookAheadDays = 3;
        ctx.AddSchedule("night", "30 23 * * *", "0 0 * * *", "e1", "e2", "e3");

        await ctx.Reconcile();

        foreach (var entry in new[] { "e1", "e2", "e3" })
        {
            Assert.Equal(3, ctx.Actions.Pending.Count(a => a.EntityId == entry && a.Action == "publish"));
            Assert.Equal(3, ctx.Actions.Pending.Count(a => a.EntityId == entry && a.Action == "unpublish"));
        }
        Assert.Equal(18, ctx.Actions.Pending.Count);
    }

    [Fact]
    public async Task PublishOnly_And_UnpublishOnly()
    {
        ctx.AddSchedule("on", "30 23 * * *", null, "e1");
        ctx.AddSchedule("off", null, "0 0 * * *", "e2");

        await ctx.Reconcile();

        Assert.Equal(
            ["create publish e1 2026-09-24T21:30:00.0000000+00:00", "create unpublish e2 2026-09-24T22:00:00.0000000+00:00"],
            ctx.Actions.Operations);
    }

    [Fact]
    public async Task SameActionFromTwoSchedules_CreatedOnce()
    {
        ctx.AddSchedule("a", "30 23 * * *", null, "e1");
        ctx.AddSchedule("b", "30 23 * * *", null, "e1");

        await ctx.Reconcile();

        Assert.Single(ctx.Actions.Operations);
    }

    [Fact]
    public async Task ExternalActions_AreNeverModified_AndDoNotCountAsOwned()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");
        ctx.Actions.Add("e1", "publish", Publish2330, createdBy: "editor");
        ctx.Actions.Add("e1", "unpublish", Unpublish0000, createdBy: "editor");
        ctx.Actions.Add("e1", "publish", Publish2330.AddHours(1), createdBy: null);

        await ctx.Reconcile();

        Assert.Equal(["create publish e1 2026-09-24T21:30:00.0000000+00:00"], ctx.Actions.Operations);
        Assert.Equal(3, ctx.Actions.Pending.Count(a => a.CreatedById != FakeContentClient.SchedulerUserId));
    }

    [Fact]
    public async Task ObsoleteOwnedActions_AreCancelled_IncludingBeyondWindow()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");
        var obsolete = ctx.Actions.Add("gone", "publish", Publish2330);
        var beyond = ctx.Actions.Add("e1", "publish", Publish2330.AddDays(5));
        ctx.Actions.Add("e1", "publish", Publish2330);

        var result = await ctx.Reconcile();

        Assert.Equal([$"cancel {obsolete.Id}", $"cancel {beyond.Id}"], ctx.Actions.Operations);
        Assert.Equal(2, result.Cancelled);
    }

    [Fact]
    public async Task ChangedCronTime_CancelsOldAndCreatesNew()
    {
        ctx.AddSchedule("night", "0 23 * * *", null, "e1");
        var old = ctx.Actions.Add("e1", "publish", Publish2330);

        await ctx.Reconcile();

        Assert.Equal([$"cancel {old.Id}", "create publish e1 2026-09-24T21:00:00.0000000+00:00"], ctx.Actions.Operations);
    }

    [Fact]
    public async Task ChangedTimeZoneOnly_UpdatesOwnedAction()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");
        var existing = ctx.Actions.Add("e1", "publish", Publish2330, timeZone: "UTC");

        var result = await ctx.Reconcile();

        Assert.Equal([$"update {existing.Id}"], ctx.Actions.Operations);
        Assert.Equal(1, result.Updated);
    }

    [Fact]
    public async Task DuplicateOwnedActions_AreReducedToOne()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");
        ctx.Actions.Add("e1", "publish", Publish2330);
        var duplicate = ctx.Actions.Add("e1", "publish", Publish2330);

        await ctx.Reconcile();

        Assert.Equal([$"cancel {duplicate.Id}"], ctx.Actions.Operations);
    }

    [Fact]
    public async Task ScheduleFailure_SkipsAllCancellations_ButStillCreatesForOthers()
    {
        var failing = ctx.AddSchedule("broken", "30 23 * * *", null, "e1");
        ctx.AddSchedule("ok", "30 23 * * *", null, "e2");
        ctx.Content.Failing.Add(failing.Selector);
        ctx.Actions.Add("e1", "publish", Publish2330); // belongs to the failing schedule

        var result = await ctx.Reconcile();

        Assert.Equal(["create publish e2 2026-09-24T21:30:00.0000000+00:00"], ctx.Actions.Operations);
        Assert.Equal(1, result.ScheduleFailures);
    }

    [Fact]
    public async Task ActionsInsideMinimumLeadTime_AreLeftAlone()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");
        ctx.Actions.Add("gone", "publish", TestContext.Now.AddSeconds(30));

        await ctx.Reconcile();

        Assert.Equal(["create publish e1 2026-09-24T21:30:00.0000000+00:00"], ctx.Actions.Operations);
    }

    [Fact]
    public async Task PendingLimit_CreatesEarliestFirst_AndCountsExternalActions()
    {
        ctx.Options.LookAheadDays = 3;
        ctx.Options.MaxPendingActions = 3;
        ctx.AddSchedule("night", "30 23 * * *", "0 0 * * *", "e1");
        ctx.Actions.Add("other", "publish", Publish2330, createdBy: "editor");

        var result = await ctx.Reconcile();

        Assert.Equal(
            ["create publish e1 2026-09-24T21:30:00.0000000+00:00", "create unpublish e1 2026-09-24T22:00:00.0000000+00:00"],
            ctx.Actions.Operations);
        Assert.Equal(4, result.DeferredByLimit);
    }

    [Fact]
    public async Task DryRun_ChangesNothing()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");
        ctx.Actions.Add("gone", "publish", Publish2330);

        var result = await ctx.Reconcile(dryRun: true);

        Assert.Empty(ctx.Actions.Operations);
        Assert.Equal((1, 1), (result.Created, result.Cancelled));
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ctx.CreateReconciler().Reconcile(cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public void FormatTime_ShowsScheduleLocalTimeAndUtc()
    {
        Assert.Equal("2026-09-27 00:00 Europe/Stockholm (2026-09-26T22:00:00Z)",
            ContentScheduleReconciler.FormatTime(new DateTimeOffset(2026, 9, 26, 22, 0, 0, TimeSpan.Zero), "Europe/Stockholm"));
        Assert.Equal("2026-09-26T22:00:00Z", ContentScheduleReconciler.FormatTime(new DateTimeOffset(2026, 9, 26, 22, 0, 0, TimeSpan.Zero), null));
    }
}
