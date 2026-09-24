using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Activout.C9.Scheduler.Redis.Tests;

public sealed class RedisFixture : IAsyncLifetime
{
    private RedisContainer? container;

    public IConnectionMultiplexer Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync();
            Connection = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
        }
        catch (Exception) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            // No Docker locally: DockerFact tests are skipped, so the fixture is never used.
        }
    }

    public async Task DisposeAsync()
    {
        Connection?.Dispose();
        if (container is not null) await container.DisposeAsync();
    }
}

public class RedisContentSchedulerLockTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private static string Name() => "content-scheduler:test:" + Guid.NewGuid().ToString("N");

    private IContentSchedulerLock NewInstance() => new RedisContentSchedulerLock(redis.Connection);

    [DockerFact]
    public async Task FirstAcquires_SecondFailsImmediately()
    {
        var name = Name();
        await using var first = await NewInstance().TryAcquire(name, Lease, default);
        var second = await NewInstance().TryAcquire(name, Lease, default);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [DockerFact]
    public async Task OwnerReleases_ThenOthersCanAcquire()
    {
        var name = Name();
        var first = await NewInstance().TryAcquire(name, Lease, default);
        await first!.DisposeAsync();

        await using var second = await NewInstance().TryAcquire(name, Lease, default);

        Assert.NotNull(second);
    }

    [DockerFact]
    public async Task ExpiredLease_PermitsAcquisition_AndStaleOwnerCannotRelease()
    {
        var name = Name();
        var stale = await NewInstance().TryAcquire(name, TimeSpan.FromMilliseconds(200), default);
        await Task.Delay(500);

        await using var current = await NewInstance().TryAcquire(name, Lease, default);
        Assert.NotNull(current);

        await stale!.DisposeAsync(); // not the owner any more: must not release current's lock
        Assert.Null(await NewInstance().TryAcquire(name, Lease, default));
    }

    [DockerFact]
    public void Registration_ReplacesDefaultLock()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(redis.Connection)
            .AddContentScheduler(_ => { })
            .AddContentSchedulerRedisLock()
            .BuildServiceProvider();

        Assert.IsType<RedisContentSchedulerLock>(provider.GetRequiredService<IContentSchedulerLock>());
    }
}
