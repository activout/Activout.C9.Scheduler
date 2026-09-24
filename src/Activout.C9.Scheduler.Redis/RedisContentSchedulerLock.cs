using StackExchange.Redis;

namespace Activout.C9.Scheduler.Redis;

/// <summary>
/// Redis-backed <see cref="IContentSchedulerLock"/>: atomic <c>SET NX PX</c> with a random owner token,
/// released only by its owner, never waits.
/// </summary>
public sealed class RedisContentSchedulerLock(IConnectionMultiplexer redis) : IContentSchedulerLock
{
    /// <inheritdoc />
    public async Task<IAsyncDisposable?> TryAcquire(string name, TimeSpan leaseTime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        return await database.LockTakeAsync(name, token, leaseTime) ? new Handle(database, name, token) : null;
    }

    private sealed class Handle(IDatabase database, RedisKey key, RedisValue token) : IAsyncDisposable
    {
        // LockRelease only deletes the key if it still holds our token, so an expired lease
        // re-acquired by another instance is left alone.
        public async ValueTask DisposeAsync() => await database.LockReleaseAsync(key, token);
    }
}
