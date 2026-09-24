using Activout.C9.Scheduler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

const string usage = """
    Usage: content-scheduler [--config <file.json>] [--dry-run] [--verbose] [--ContentScheduler:Key=value ...]

    Runs one reconciliation of the "ContentScheduler" configuration section against Contentful.
    Configuration sources, later wins: <file.json> (default: appsettings.json in the current
    directory, optional), environment variables (e.g. ContentScheduler__ManagementToken),
    command-line overrides.

      --dry-run   Log planned creates/updates/cancels without changing anything.
      --verbose   Debug logging.

    Exit codes: 0 success, 1 some operations or schedules failed, 2 invalid usage or configuration.
    """;

var configFile = "appsettings.json";
var dryRun = false;
var verbose = false;
var overrides = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--dry-run": dryRun = true; break;
        case "--verbose": verbose = true; break;
        case "--config" when i + 1 < args.Length: configFile = args[++i]; break;
        case "-h" or "--help":
            Console.WriteLine(usage);
            return 0;
        default:
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || !args[i].Contains('='))
            {
                Console.Error.WriteLine($"Unknown argument: {args[i]}\n\n{usage}");
                return 2;
            }
            overrides.Add(args[i]);
            break;
    }
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.GetFullPath(configFile), optional: configFile == "appsettings.json")
    .AddEnvironmentVariables()
    .AddCommandLine(overrides.ToArray())
    .Build();

var services = new ServiceCollection()
    .AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information)
        .AddFilter("System.Net.Http", verbose ? LogLevel.Information : LogLevel.Warning))
    .AddContentScheduler(configuration.GetSection("ContentScheduler"));

await using var provider = services.BuildServiceProvider();

try
{
    _ = provider.GetRequiredService<IOptions<ContentSchedulerOptions>>().Value;
}
catch (OptionsValidationException ex)
{
    Console.Error.WriteLine("Invalid ContentScheduler configuration:");
    foreach (var failure in ex.Failures) Console.Error.WriteLine($"  {failure}");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var result = await provider.GetRequiredService<ContentScheduleReconciler>().Reconcile(dryRun, cts.Token);
    return result.Failed > 0 || result.ScheduleFailures > 0 ? 1 : 0;
}
catch (Exception ex)
{
    provider.GetRequiredService<ILogger<Program>>().LogError(ex, "Reconciliation failed");
    return 1;
}
