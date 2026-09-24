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

All replicas run the same worker; only the one acquiring the lock performs a given run, the others
skip it (they never queue).

## Configuration

```json
{
  "ContentScheduler": {
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

```bash
dotnet tool install --global Activout.C9.Scheduler.Cli

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
