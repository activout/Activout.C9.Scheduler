namespace Activout.C9.Scheduler;

/// <summary>Default single-instance lock: acquisition always succeeds.</summary>
internal sealed class AlwaysAvailableContentSchedulerLock : IContentSchedulerLock
{
    // Separate handle type: the lock itself must not be IAsyncDisposable-only, or synchronous
    // disposal of the DI container throws.
    private sealed class NoOpHandle : IAsyncDisposable
    {
        public static readonly NoOpHandle Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public Task<IAsyncDisposable?> TryAcquire(string name, TimeSpan leaseTime, CancellationToken cancellationToken) =>
        Task.FromResult<IAsyncDisposable?>(NoOpHandle.Instance);
}
