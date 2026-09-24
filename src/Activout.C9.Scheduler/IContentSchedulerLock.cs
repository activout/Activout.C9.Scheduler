namespace Activout.C9.Scheduler;

/// <summary>
/// Coordinates reconciliation between application instances. Acquisition must never wait for
/// another holder: if the lock is taken, return <c>null</c> immediately and the run is skipped.
/// </summary>
public interface IContentSchedulerLock
{
    /// <summary>
    /// Tries to acquire the lock <paramref name="name"/>.
    /// </summary>
    /// <returns>
    /// A handle that releases the lock when disposed (or when the lease expires), or <c>null</c>
    /// if the lock was not acquired.
    /// </returns>
    Task<IAsyncDisposable?> TryAcquire(string name, TimeSpan leaseTime, CancellationToken cancellationToken);
}
