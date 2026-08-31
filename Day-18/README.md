# Day 18 — Background jobs

Move slow work off the request thread. Built on top of the Week-1
`QuotesApi` (copied here from `Day-5/QuotesApi`) and its Angular front end
(copied here from `Day-17/quotes-angular`) — see "What was copied, and why"
below.

## The feature

`POST /api/quotes/import` enqueues a job that imports N quotes from the
external quote service (one HTTP call + one DB write per quote) and returns
**immediately** with `202 Accepted` and a job id. The import itself never
runs on the request thread — it runs on a dedicated background queue,
draining one item at a time, while the caller polls `GET /api/jobs/{id}`
for progress.

```
POST /api/quotes/import {"count":3}
  -> 202 Accepted {"id": "...", "status": "Queued", ...}   <-- instant
GET /api/jobs/{id}
  -> {"status": "Running", "importedCount": 1, "requestedCount": 3}
  -> {"status": "Completed", "importedCount": 3, "requestedCount": 3}
```

The Angular app's **Background Jobs** panel (bottom-left) drives this
directly: enqueue a job, watch it queue → run → complete in real time.

## The three approaches, contrasted

| | **BackgroundService** (`QueuedHostedService`) | **IHostedService** (`PeriodicStatsHostedService`) | **Hangfire** (`HangfireRecurringJobs`) |
|---|---|---|---|
| What it's for here | Draining an in-process work queue — event-driven, arbitrary work items | A fixed-interval loop — scheduled, but nothing to queue | A cron-scheduled recurring job, with history and a dashboard |
| Boilerplate you write | Override `ExecuteAsync(stoppingToken)`; the base class owns the `CancellationTokenSource`, the `Task` field, and the bounded wait-for-shutdown in `StopAsync` | You own all of it by hand: your own CTS, your own `Task` field, your own `StopAsync` that cancels and then awaits the loop with a timeout (see `PeriodicStatsHostedService.StopAsync`) | None — Hangfire's server does the scheduling, execution, and retry loop; you only write the job method |
| Persistence | In-memory only (the `Channel<T>` and the `IJobStatusStore` are process memory) — a restart loses every queued/running job | In-memory only — same restart risk | In-memory here too (`UseInMemoryStorage()`), **but** swapping in SQL Server/PostgreSQL/Redis storage makes the *schedule itself* survive a restart and lets multiple instances share one queue, with zero code change to the job method |
| Observability | Only what you log yourself | Only what you log yourself | A dashboard (`/hangfire`) with per-run history, duration, retries, and a "Trigger now" button, for free |
| Scaling across instances | No — each instance drains its own in-memory queue | No | Yes, with persistent storage — Hangfire coordinates so only one instance runs a given occurrence |
| Best fit | Fire-and-forget work triggered by a request, kept simple on purpose | A trivial one-off timer where pulling in Hangfire would be overkill | Anything that needs to survive a restart, run on a real schedule, retry on failure, or be inspectable after the fact |

The practical takeaway: **`BackgroundService` is `IHostedService` plus the
CTS/Task/StopAsync boilerplate written once, correctly, in the framework** —
compare `QueuedHostedService.cs` (just an `ExecuteAsync` override) against
`PeriodicStatsHostedService.cs` (the same idea, by hand). And for genuinely
**scheduled** work, Hangfire beats both hand-rolled hosted services the
moment you need the schedule to survive a restart, run across more than one
instance, or be inspectable without grepping logs.

## Graceful shutdown

`QueuedHostedService.ExecuteAsync` is handed `stoppingToken` by the
`BackgroundService` base class, and the host cancels that token the instant
`Ctrl+C`/`SIGINT` (or a container orchestrator's `SIGTERM`) begins shutdown.
The import work item threads that same token through every one of its
awaits (`Task.Delay`, the external HTTP call, `SaveChangesAsync`), so
cancellation is cooperative and immediate — whichever step is in flight when
shutdown starts is cut short cleanly (not killed forcibly), and the item's
own `catch (OperationCanceledException)` records a clear terminal state:

```csharp
catch (OperationCanceledException)
{
    job.Status = BackgroundJobStatus.Failed;
    job.Error = $"Cancelled during graceful shutdown after importing " +
                $"{job.ImportedCount} of {job.RequestedCount} quote(s).";
}
```

`Program.cs` also raises `HostOptions.ShutdownTimeout` to 20s (the default
is 5s), giving the loop's own bookkeeping/logging room to finish even though,
as built, the current item doesn't get a separate grace window — it's
cancelled at the same instant as everything still sitting in the queue.

**Real captured evidence**, from actually sending `SIGINT` to a running
instance mid-job (`results/graceful-shutdown-demo.log`; a second run that
happened to land mid-network-call instead of mid-delay is in
`results/import-job-run.log`, with the resulting stack trace from the
external HTTP client's own retry-exhausted log):

```
[10:33:33 INF] QueuedHostedService started; draining the background task queue.
[10:33:34 INF] HTTP POST /api/quotes/import responded 202 in 173.2ms      <-- instant, work not done yet
[10:33:35 INF] Created quote 8 by Walt Disney                             <-- item 2 of 4 just wrote to the DB
[10:33:36 INF] Server ... caught stopping signal...
[10:33:36 INF] Application is shutting down...
[10:33:36 INF] Graceful shutdown requested for QueuedHostedService.
              Letting any in-flight work item finish (queue currently holds 0 pending item(s)).
[10:33:36 INF] QueuedHostedService loop exited.
[10:33:36 INF] QueuedHostedService stopped.
```

Two things worth calling out, both visible above:

- The `POST` returned in 173ms while the job needed several more seconds —
  proof the request thread never waited on the slow work.
- No corrupted or partial row was left behind: each quote's `AddAsync` +
  `SaveChangesAsync` is a single atomic unit, so a cancellation lands
  *between* quotes, never mid-write.
- One real limitation, also confirmed live: once shutdown begins Kestrel
  stops accepting new connections almost immediately, so `GET /api/jobs/{id}`
  can no longer be polled for the job's *final* state — only the server's
  own log has the full story. That is a direct consequence of the job store
  being in-process memory with no separate read path, and it's exactly the
  gap a persisted store (Hangfire's, or a real database-backed job table)
  closes.

## What was copied, and why

Per the brief: nothing outside `Day-18/` was modified. Everything Day-18
depends on was **copied into** `Day-18/` first, then adapted here:

- `Day-18/QuotesApi/` — copied from `Day-5/QuotesApi` (the most evolved
  backend in the course: EF Core + Sqlite, JWT auth, Serilog, OpenTelemetry,
  a resilient external HTTP client). Added: `BackgroundJobs/` (the queue,
  both hosted services, the Hangfire job), `Extensions/BackgroundJobEndpointExtensions.cs`,
  and the Hangfire/queue wiring in `Program.cs`/`InfrastructureExtensions.cs`.
- `Day-18/quotes-angular/` — copied from `Day-17/quotes-angular` (the most
  evolved UI: routing, guards, the signals-based store, the API-activity
  inspector). Added: `background-jobs-panel/` (component + template +
  styles), `services/background-jobs-api.ts`, `models/background-job.model.ts`,
  and wired the new panel into `app.ts`/`app.html` next to the existing
  activity panel.

**One bug found and fixed in the copied backend**, disclosed here rather
than silently patched: `ExternalQuoteClient` called `quotes/random` against
a base address that was already `https://dummyjson.com/quotes/`, producing
a 404 (`.../quotes/quotes/random`), and `ExternalQuote.Text` had no
`[JsonPropertyName]` to match DummyJSON's actual field name (`quote`, not
`text`) — so even a correct URL would have deserialized an empty quote body.
Both were confirmed live against the real DummyJSON API before fixing.
Fixed only in this folder's copy (`Day-18/QuotesApi/ExternalQuotes/`);
`Day-5/QuotesApi` was left untouched.

**One new secret generated, not reused**: the JWT signing key that the
original `dev-token.local.json` was signed with is a developer secret that
was never committed to the repo (`dotnet user-secrets list` in `Day-5`
confirms it isn't there). Day-18 generates and stores its own local-only
`Jwt:Key` via `dotnet user-secrets set` and ships a `dev-token.local.json`
signed with it — same role (a "quotes.write"-scoped dev bearer token), same
issuer/audience the API expects, independently generated.

## Running it locally

```bash
# Terminal 1 — backend
cd Day-18/QuotesApi
dotnet run --urls http://localhost:5147
# Hangfire dashboard: http://localhost:5147/hangfire

# Terminal 2 — frontend
cd Day-18/quotes-angular
npm install
npm start   # ng serve, proxies /api to :5147 (see proxy.conf.json)
# http://localhost:4200 — sign in, then use the "Background Jobs" panel
```

To see the graceful-shutdown behavior yourself: enqueue an import job with
a few quotes, then `Ctrl+C` the backend terminal while it's mid-job, and
watch the console log the same sequence shown above.

## Screenshots (`screenshots/`)

- `day18-01-quotes-signed-in.png` — signed in, both panels available
- `day18-02-jobs-panel-empty.png` — Background Jobs panel opened, no jobs yet
- `day18-03-jobs-panel-running.png` — a job enqueued and mid-drain (`0/3 imported`)
- `day18-04-jobs-panel-queued-plus-running.png` — a second job enqueued while the first is still running
- `day18-05-jobs-panel-completed.png` — both jobs settled (`3/3 imported` each), proving the queue drained them one at a time
- `day18-06-hangfire-dashboard.png` — Hangfire's overview/history graph
- `day18-07-hangfire-recurring-jobs.png` — the `quote-digest` recurring job, cron schedule and last/next execution
- `day18-08-hangfire-succeeded-jobs.png` — three real recurring-job executions, with duration/latency

## Files added

```
Day-18/QuotesApi/BackgroundJobs/
  BackgroundJobStatus.cs         enum: Queued/Running/Completed/Failed
  QuoteImportJob.cs              in-memory job record (mutable, shared by reference)
  IJobStatusStore.cs / JobStatusStore.cs      singleton job tracker
  IBackgroundTaskQueue.cs / BackgroundTaskQueue.cs   the Channel<T>-backed queue
  QueuedHostedService.cs         BackgroundService draining the queue
  PeriodicStatsHostedService.cs  contrast: plain IHostedService, hand-rolled timer
  HangfireRecurringJobs.cs       contrast: Hangfire recurring job method
Day-18/QuotesApi/Extensions/BackgroundJobEndpointExtensions.cs
  POST /api/quotes/import, GET /api/jobs/{id}

Day-18/quotes-angular/src/app/background-jobs-panel/
  background-jobs-panel.ts/.html/.css
Day-18/quotes-angular/src/app/services/background-jobs-api.ts
Day-18/quotes-angular/src/app/models/background-job.model.ts
```
