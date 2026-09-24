using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Activout.C9.Scheduler.Redis;

/// <summary>Registration of the Redis scheduler lock.</summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the default single-instance lock with <see cref="RedisContentSchedulerLock"/>.
    /// Requires an <see cref="StackExchange.Redis.IConnectionMultiplexer"/> to be registered by the application.
    /// </summary>
    public static IServiceCollection AddContentSchedulerRedisLock(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IContentSchedulerLock, RedisContentSchedulerLock>());
        return services;
    }
}
