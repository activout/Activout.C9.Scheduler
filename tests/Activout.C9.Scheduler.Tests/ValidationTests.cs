using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Activout.C9.Scheduler.Tests;

public class ValidationTests
{
    private static ContentSchedulerOptions Valid() => new()
    {
        SpaceId = "space",
        Environment = "master",
        ManagementToken = "token",
        ReconcileCron = "*/10 * * * *",
        Schedules =
        [
            new ContentSchedule
            {
                Name = "night", Selector = new ContentSelector { Tag = "night-content" },
                TimeZone = "Europe/Stockholm", Publish = "30 23 * * *", Unpublish = "0 0 * * *",
            },
        ],
    };

    private static IEnumerable<string> Errors(ContentSchedulerOptions options) =>
        new ContentSchedulerOptionsValidator().Validate(null, options).Failures ?? [];

    [Fact]
    public void ValidConfiguration_Passes() => Assert.Empty(Errors(Valid()));

    [Fact]
    public void EmptySelector_IsRejected()
    {
        var o = Valid();
        o.Schedules[0].Selector = new ContentSelector { Tag = " " };
        Assert.Contains(Errors(o), e => e.Contains("Selector must set at least one"));
    }

    [Theory]
    [InlineData("SpaceId")]
    [InlineData("Environment")]
    [InlineData("ManagementToken")]
    [InlineData("ReconcileCron")]
    public void MissingRequired_IsRejected(string property)
    {
        var o = Valid();
        typeof(ContentSchedulerOptions).GetProperty(property)!.SetValue(o, "");
        Assert.Contains(Errors(o), e => e.StartsWith(property));
    }

    [Fact]
    public void InvalidValues_AreRejected()
    {
        var o = Valid();
        o.LookAheadDays = 0;
        o.ManagementApiBaseUrl = "http://api.eu.contentful.com";
        o.ReconcileCron = "not cron";
        o.Schedules[0].Publish = "99 * * * *";
        o.Schedules[0].TimeZone = "Mars/Olympus";
        o.Schedules = [o.Schedules[0], new ContentSchedule { Name = "night", Selector = new ContentSelector { EntryId = "x" }, TimeZone = "UTC" }];

        var errors = Errors(o).ToList();

        Assert.Contains(errors, e => e.StartsWith("LookAheadDays"));
        Assert.Contains(errors, e => e.StartsWith("ManagementApiBaseUrl"));
        Assert.Contains(errors, e => e.StartsWith("ReconcileCron: invalid"));
        Assert.Contains(errors, e => e.Contains("Publish: invalid"));
        Assert.Contains(errors, e => e.Contains("Mars/Olympus"));
        Assert.Contains(errors, e => e.Contains("duplicate schedule name"));
        Assert.Contains(errors, e => e.Contains("at least one of Publish or Unpublish"));
    }

    [Fact]
    public void BindsFromConfigurationSection_AndValidatesThroughDi()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ContentScheduler:SpaceId"] = "space",
            ["ContentScheduler:Environment"] = "master",
            ["ContentScheduler:ManagementToken"] = "token",
            ["ContentScheduler:ReconcileCron"] = "*/10 * * * *",
            ["ContentScheduler:LockLease"] = "00:05:00",
            ["ContentScheduler:ManagementApiBaseUrl"] = "https://api.eu.contentful.com",
            ["ContentScheduler:Schedules:0:Name"] = "night",
            ["ContentScheduler:Schedules:0:Selector:Tag"] = "night-content",
            ["ContentScheduler:Schedules:0:Selector:ContentType"] = "campaignPage",
            ["ContentScheduler:Schedules:0:TimeZone"] = "Europe/Stockholm",
            ["ContentScheduler:Schedules:0:Publish"] = "30 23 * * *",
        }).Build();

        using var provider = new ServiceCollection().AddLogging()
            .AddContentScheduler(configuration.GetSection("ContentScheduler")).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ContentSchedulerOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(5), options.LockLease);
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpScheduledActionsClient.HttpClientName);
        Assert.Equal(new Uri("https://api.eu.contentful.com/"), http.BaseAddress);
        Assert.Equal("campaignPage", Assert.Single(options.Schedules).Selector.ContentType);
        Assert.NotNull(provider.GetRequiredService<ContentScheduleReconciler>());
        Assert.IsType<AlwaysAvailableContentSchedulerLock>(provider.GetRequiredService<IContentSchedulerLock>());
    }

    [Fact]
    public void InvalidConfiguration_FailsThroughDi()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddContentScheduler(o => o.SpaceId = "space").BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ContentSchedulerOptions>>().Value);
    }
}
