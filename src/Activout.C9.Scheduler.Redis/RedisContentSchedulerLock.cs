using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Activout.C9.Scheduler.Redis;

/// <summary>
/// Redis-backed <see cref="IContentSchedulerLock"/>: atomic <c>SET NX PX</c> with a random owner token,
/// released only by its owner, never waits.
/// </summary>
/// <param name="redis">Redis connection.</param>
/// <param name="options">Optional configuration, e.g. the key prefix.</param>
public sealed class RedisContentSchedulerLock(
    IConnectionMultiplexer redis,
    IOptions<RedisContentSchedulerLockOptions>? options = null) : IContentSchedulerLock
{
    private readonly string keyPrefix = options?.Value.KeyPrefix ?? "";

    /// <inheritdoc />
    public async Task<IAsyncDisposable?> TryAcquire(string name, TimeSpan leaseTime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = redis.GetDatabase();
        RedisKey key = keyPrefix + name;
        var token = Guid.NewGuid().ToString("N");
        return await database.LockTakeAsync(key, token, leaseTime) ? new Handle(database, key, token) : null;
    }

    private sealed class Handle(IDatabase database, RedisKey key, RedisValue token) : IAsyncDisposable
    {
        // LockRelease only deletes the key if it still holds our token, so an expired lease
        // re-acquired by another instance is left alone.
        public async ValueTask DisposeAsync() => await database.LockReleaseAsync(key, token);
    }
}
