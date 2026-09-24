using Activout.C9.Scheduler.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Activout.C9.Scheduler.Tests;

public class WorkerTests
{
    private sealed class UnavailableLock : IContentSchedulerLock
    {
        public string? RequestedName { get; private set; }

        public Task<IAsyncDisposable?> TryAcquire(string name, TimeSpan leaseTime, CancellationToken cancellationToken)
        {
            RequestedName = name;
            return Task.FromResult<IAsyncDisposable?>(null);
        }
    }

    private readonly TestContext ctx = new();

    private ContentSchedulerWorker CreateWorker(IContentSchedulerLock schedulerLock) =>
        new(ctx.CreateReconciler(), schedulerLock, Microsoft.Extensions.Options.Options.Create(ctx.Options), ctx.Time,
            NullLogger<ContentSchedulerWorker>.Instance);

    [Fact]
    public async Task DefaultLock_AlwaysSucceeds()
    {
        var schedulerLock = new AlwaysAvailableContentSchedulerLock();
        await using var first = await schedulerLock.TryAcquire("x", TimeSpan.FromMinutes(1), default);
        await using var second = await schedulerLock.TryAcquire("x", TimeSpan.FromMinutes(1), default);
        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task LockUnavailable_SkipsReconciliation()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");
        var schedulerLock = new UnavailableLock();

        await CreateWorker(schedulerLock).RunOnce(default);

        Assert.Equal("content-scheduler:space:master", schedulerLock.RequestedName);
        Assert.Equal(0, ctx.Content.Calls);
        Assert.Empty(ctx.Actions.Operations);
    }

    [Fact]
    public async Task LockAcquired_Reconciles()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");

        await CreateWorker(new AlwaysAvailableContentSchedulerLock()).RunOnce(default);

        Assert.Single(ctx.Actions.Operations);
    }

    [Fact]
    public async Task ReconciliationFailure_IsLoggedNotThrown()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");
        ctx.Content.IdentityFails = true;

        await CreateWorker(new AlwaysAvailableContentSchedulerLock()).RunOnce(default);

        Assert.Empty(ctx.Actions.Operations);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateWorker(new AlwaysAvailableContentSchedulerLock()).RunOnce(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Worker_RunsAtStartupAndThenOnCron()
    {
        ctx.AddSchedule("night", "30 23 * * *", null, "e1");
        using var cts = new CancellationTokenSource();
        var worker = CreateWorker(new AlwaysAvailableContentSchedulerLock());

        await worker.StartAsync(cts.Token);
        await WaitFor(() => ctx.Content.Calls == 2); // startup run: resolve + identity
        ctx.Time.Advance(TimeSpan.FromMinutes(10));
        await WaitFor(() => ctx.Content.Calls == 4);
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(ctx.Actions.Operations);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}
