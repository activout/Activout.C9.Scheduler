using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Activout.C9.Scheduler;

/// <summary>Registration of the content scheduler.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the content scheduler, binding <see cref="ContentSchedulerOptions"/> from <paramref name="configuration"/>
    /// (typically <c>Configuration.GetSection("ContentScheduler")</c>). Options are validated at startup.
    /// </summary>
    public static IServiceCollection AddContentScheduler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ContentSchedulerOptions>().Bind(configuration);
        return services.AddContentSchedulerCore();
    }

    /// <summary>Adds the content scheduler, configuring <see cref="ContentSchedulerOptions"/> in code.</summary>
    public static IServiceCollection AddContentScheduler(this IServiceCollection services, Action<ContentSchedulerOptions> configure)
    {
        services.AddOptions<ContentSchedulerOptions>().Configure(configure);
        return services.AddContentSchedulerCore();
    }

    private static IServiceCollection AddContentSchedulerCore(this IServiceCollection services)
    {
        services.AddOptions<ContentSchedulerOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ContentSchedulerOptions>, ContentSchedulerOptionsValidator>());

        services.AddHttpClient(ContentfulContentClient.HttpClientName);
        services.AddHttpClient(HttpScheduledActionsClient.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var o = sp.GetRequiredService<IOptions<ContentSchedulerOptions>>().Value;
                client.BaseAddress = o.ManagementApiBaseUri;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ManagementToken);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(HttpScheduledActionsClient.MediaType));
            });

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IContentSchedulerLock, AlwaysAvailableContentSchedulerLock>();
        services.TryAddSingleton<IContentClient, ContentfulContentClient>();
        services.TryAddSingleton<IScheduledActionsClient, HttpScheduledActionsClient>();
        services.TryAddSingleton(sp => new ContentScheduleReconciler(
            sp.GetRequiredService<IContentClient>(),
            sp.GetRequiredService<IScheduledActionsClient>(),
            sp.GetRequiredService<IOptions<ContentSchedulerOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ContentScheduleReconciler>>()));
        services.AddHostedService<ContentSchedulerWorker>();
        return services;
    }
}
