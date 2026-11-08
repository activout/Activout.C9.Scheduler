# Activout.C9.Scheduler

Declaratively manages future **publish** and **unpublish** Scheduled Actions for Contentful entries.

You describe *which* entries (by tag, content type and/or entry ID) should be published/unpublished
*when* (cron expressions in an explicit time zone). A background service periodically reconciles
that desired state against Contentful's
[Scheduled Actions](https://www.contentful.com/developers/docs/references/content-management-api/#/reference/scheduled-actions)
so that every required action exists within a look-ahead window. Contentful executes the actions;
this library never publishes or unpublishes anything itself.

| Package | Purpose |
|---|---|
| `Activout.C9.Scheduler` | Core library: configuration, reconciliation, hosted background service |
| `Activout.C9.Scheduler.Redis` | Optional Redis lock for replicated deployments |
| `Activout.C9.Scheduler.Cli` | `dotnet` tool that runs one reconciliation (no background service) |

## Installation

```bash
dotnet add package Activout.C9.Scheduler
dotnet add package Activout.C9.Scheduler.Redis   # only for multiple replicas
```

## Usage

```csharp
builder.Services.AddContentScheduler(builder.Configuration.GetSection("ContentScheduler"));
```

Replicated backend (requires an `IConnectionMultiplexer` registered by your application):

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("redis:6379"));
builder.Services.AddContentScheduler(builder.Configuration.GetSection("ContentScheduler"));
builder.Services.AddContentSchedulerRedisLock();
```

To share a Redis instance with other applications, prefix the lock key (default: no prefix,
key is `content-scheduler:{SpaceId}:{Environment}`):

```csharp
builder.Services.AddContentSchedulerRedisLock(o => o.KeyPrefix = "myapp:");
```

All replicas run the same worker; only the one acquiring the lock performs a given run, the others
skip it (they never queue).

## Configuration

```json
{
  "ContentScheduler": {
    "Enabled": true,
    "SpaceId": "space-id",
    "Environment": "master",
    "ManagementToken": "secret",
    "ReconcileCron": "*/10 * * * *",
    "LookAheadDays": 7,
    "LockLease": "00:02:00",
    "MaxPendingActions": 500,
    "Schedules": [
      {
        "Name": "NightContent",
        "Selector": { "Tag": "night-content", "ContentType": "campaignPage" },
        "TimeZone": "Europe/Stockholm",
        "Publish": "30 23 * * *",
        "Unpublish": "0 0 * * *"
      }
    ]
  }
}
```

| Setting | Meaning |
|---|---|
| `Enabled` | Set to `false` to turn the scheduler off in an environment (default `true`). The worker and the CLI then do nothing, and the other settings are neither required nor validated. |
| `SpaceId`, `Environment` | Contentful space and environment ID |
| `ManagementApiBaseUrl` | CMA base URL. Default `https://api.contentful.com/`; use `https://api.eu.contentful.com/` for EU data residency |
| `ManagementToken` | CMA token of a **dedicated** scheduler identity (see below). Never logged. |
| `ReconcileCron` | How often reconciliation runs, evaluated in **UTC**. It also runs once at startup. |
| `LookAheadDays` | How far ahead actions are maintained (default 7) |
| `LockLease` | Lease for the distributed lock (default 2 minutes) |
| `MaxPendingActions` | Cap on pending actions in the environment, owned + external (default 500, Contentful's limit) |
| `Schedules[].Name` | Unique name, used in logs |
| `Schedules[].Selector` | `Tag` (tag **ID**), `ContentType`, `EntryId`, combined with **AND**. At least one is required; an empty selector is rejected, never "all entries". For OR, define several schedules. |
| `Schedules[].TimeZone` | IANA time zone the cron expressions are evaluated in; DST is handled |
| `Schedules[].Publish` / `Unpublish` | Standard 5-field cron expressions; at least one is required |

Configuration is validated at startup; invalid configuration fails fast with all errors listed.

Contentful stores action times in UTC. Logs show both, e.g.
`would create unpublish of entry abc at 2026-09-27 00:00 Europe/Stockholm (2026-09-26T22:00:00Z)`.

## How reconciliation works

Each run computes the desired set of actions (entry × action × UTC time) for all occurrences between
now + 1 minute and now + `LookAheadDays`, reads all pending Scheduled Actions of the environment,
and then, **for actions created by the scheduler identity only**:

- matching actions are left alone (or updated if only the time zone differs),
- missing actions are created, earliest first, within `MaxPendingActions`,
- actions no longer desired (removed schedule, changed cron, entry no longer matching, duplicates) are cancelled.

Actions created by anyone else are never modified, cancelled or deleted. No state is stored
anywhere except in Contentful, so the process is idempotent and recovers from crashes, partial
runs and configuration changes by simply running again. If resolving entries for any schedule
fails, that run performs no cancellations (it can't tell which actions are still wanted).
Archived entries never match. Actions due within the next minute are never touched.

### Operational requirement: a dedicated identity

Ownership is determined **exclusively** by `sys.createdBy` of each Scheduled Action, compared with
the user owning `ManagementToken`. Use a token that belongs to a Contentful user dedicated to the
scheduler, not shared with editors, other integrations or manual tooling. Otherwise the scheduler
will treat their Scheduled Actions as its own and cancel them.

Note that a scheduled **publish** publishes the entry's *current* draft at that time, including
any edits made since.

## CLI

### Download

On a Mac (M1 or newer) or Linux with [Homebrew](https://brew.sh), install and update it with:

```sh
brew install activout/tap/content-scheduler
brew upgrade content-scheduler
```

Otherwise, get the file for your computer from the [latest release](https://github.com/activout/Activout.C9.Scheduler/releases/latest). It is a single
program with nothing else to install. The files are not code signed yet, so your computer warns you the first time.

- **Windows:** download `content-scheduler-win-x64.exe`, rename it to `content-scheduler.exe` and open PowerShell in that folder. If Windows
  says "Windows protected your PC", click **More info**, then **Run anyway**. Type `.\content-scheduler.exe` where this README
  says `content-scheduler`.
- **Mac (M1 or newer):** download `content-scheduler-osx-arm64.tar.gz`, then in Terminal:
  ```sh
  cd ~/Downloads
  tar -xzf content-scheduler-osx-arm64.tar.gz
  xattr -d com.apple.quarantine content-scheduler
  sudo mv content-scheduler /usr/local/bin/
  ```
- **Linux:** download `content-scheduler-linux-x64.tar.gz`, run `tar -xzf content-scheduler-linux-x64.tar.gz` and move `content-scheduler` to a folder on
  your `PATH`.

Check a download against the release's `SHA256SUMS`, or verify its build provenance with
`gh attestation verify <file> -R activout/Activout.C9.Scheduler`.

With .NET 10 installed you can use the .NET tool instead: `dotnet tool install -g Activout.C9.Scheduler.Cli`.

### Use

```bash
content-scheduler --config scheduler.json --dry-run    # show what would change
content-scheduler --config scheduler.json              # reconcile once
```

Configuration is read from the JSON file (default `appsettings.json`, section `ContentScheduler`),
environment variables (`ContentScheduler__ManagementToken=...`) and `--ContentScheduler:Key=value`
overrides. Exit code 0 on success, 1 if any operation or schedule failed, 2 on invalid configuration.
The CLI does not take the distributed lock; running it alongside the service is safe because
reconciliation is idempotent.

## License

MIT
