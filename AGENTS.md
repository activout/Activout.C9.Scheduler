# AGENTS.md

Guidance for AI coding agents working in this repository.

## What this is

`Activout.C9.Scheduler` is a .NET 10 library that reconciles cron-based publish/unpublish rules
into Contentful Scheduled Actions. It never publishes/unpublishes directly and keeps no state
outside Contentful.

## Layout

```
src/Activout.C9.Scheduler/          core (namespace Activout.C9.Scheduler)
  Content/                          entry resolution via the official Contentful SDK (IContentClient)
  ScheduledActions/                 HttpClient adapter for the Scheduled Actions CMA API (IScheduledActionsClient)
  ContentScheduleReconciler.cs      desired-state diff + apply
  ContentSchedulerWorker.cs         BackgroundService: startup run + ReconcileCron (UTC) loop
src/Activout.C9.Scheduler.Redis/    Redis IContentSchedulerLock
src/Activout.C9.Scheduler.Cli/      dotnet tool running one reconciliation
tests/Activout.C9.Scheduler.Tests/  xUnit; Support/Fakes.cs has in-memory CMA fakes
tests/Activout.C9.Scheduler.Redis.Tests/  Testcontainers; [DockerFact] skips locally without Docker, never in CI
```

## Build / test / pack

```bash
dotnet build -c Release      # warnings are errors
dotnet test -c Release
dotnet pack -c Release -o artifacts
```

## Rules

- Ownership is `sys.createdBy == current user` only. Never mutate an action not owned.
- If any schedule fails to resolve, no cancellations happen in that run.
- Raw HTTP/JSON for Scheduled Actions stays inside `HttpScheduledActionsClient`; the SDK is used for everything else.
- No persistence, no leader election, no Quartz/Hangfire, no REST client frameworks.
- `C9` only in package/namespace names; public types use `Content*` naming.
- Async methods have no `Async` suffix. Public members need XML docs (build fails otherwise).
- The design spec is kept locally (`Activout.C9.Scheduler-spec-v3.md`, not committed).

## CI

`.github/workflows/ci.yml` runs on every push/PR: restore, build, test, pack.
`.github/workflows/publish.yml` packs and pushes all packages to NuGet.org on `v*` tags using the
`NUGET_API_KEY` repo secret.
