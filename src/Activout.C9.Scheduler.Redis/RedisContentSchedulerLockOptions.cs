namespace Activout.C9.Scheduler.Redis;

/// <summary>Configuration for <see cref="RedisContentSchedulerLock"/>.</summary>
public sealed class RedisContentSchedulerLockOptions
{
    /// <summary>
    /// Prepended verbatim to the lock name to form the Redis key, e.g. <c>myapp:</c>.
    /// Default is empty (the lock name is used as the key).
    /// </summary>
    public string KeyPrefix { get; set; } = "";
}
