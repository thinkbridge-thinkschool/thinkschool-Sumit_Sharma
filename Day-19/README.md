# Day 19 — Azure Service Bus topics + DLQ

Publish to a Service Bus topic with two subscriptions, consume with a
competing-consumer worker, make handlers idempotent (dedupe on a message
id), and demonstrate the dead-letter queue (DLQ) catching a poison
message. Built on top of the Week-1 `QuotesApi` (copied here from
`Day-18/QuotesApi`) and its Angular front end (copied here from
`Day-18/quotes-angular`) — see "What was copied, and why" below. Runs
against the real **Azure Service Bus emulator** (Docker), not a fake —
every log line and screenshot in this README is from an actual local run
against it. It is also **deployed live** against a real Azure Service Bus
namespace — see "Live deployment" below.

## Live deployment

- **App:** https://quotes-web-day19.bravebay-90a32791.centralindia.azurecontainerapps.io
- **API:** https://quotes-api-day19.bravebay-90a32791.centralindia.azurecontainerapps.io (`/health`, `/api/*`)

Everything here runs against a **real Azure Service Bus Standard namespace**
(`sb-thinkschool-day19`, `thinkschool-rg`, `centralindia`) with the same
`quotes.events` topic and two subscriptions used locally — not the emulator.
The deployment reuses the same architecture Day-17 proved out for
`quotes-web`/`quotes-api`, extended to the two new Container Apps below:

```
Browser
  │  same-origin fetch, e.g. POST /api/events/quote-created — no token, ever
  ▼
Container App: quotes-web-day19
  Express server (server/server.js) - serves the built Angular app and
  proxies /api/* to quotes-api-day19, attaching an Entra access token
  acquired via its own system-assigned Managed Identity (DefaultAzureCredential)
  for api://697131d5-940f-4d27-a027-8f2284907c64/.default. No secret,
  connection string, or certificate configured anywhere for this hop.
  ▼  Authorization: Bearer <Managed-Identity-issued Entra token>
Container App: quotes-api-day19
  EntraJwtScheme validates the token (roles contains Quotes.Write →
  satisfies CanEditQuotes). On startup, ServiceBusOptions.FullyQualifiedNamespace
  is set, so QuotesApi.Extensions.InfrastructureExtensions.AddServiceBusMessaging
  builds its ServiceBusClient with this Container App's own system-assigned
  Managed Identity (DefaultAzureCredential) instead of the emulator's dev
  connection string - see Messaging/ServiceBusOptions.cs.
  ▼  AMQP, Managed Identity token (Azure Service Bus Data Owner role)
Real Azure Service Bus namespace: sb-thinkschool-day19.servicebus.windows.net
  Topic quotes.events → audit-log / digest-notifications subscriptions,
  same MaxDeliveryCount=3, same fan-out, same DLQ behavior verified locally.
```

Verified live, over the real internet, through this exact chain (not just
against the API directly):

```
$ curl -sS -X POST https://quotes-web-day19.../api/events/quote-created \
    -d '{"quoteId":2,"author":"Full Chain","text":"Browser to Managed Identity to Entra to real Service Bus."}'
{"eventId":"de7ecae7-...","quoteId":2,"author":"Full Chain", ...}   # 202 Accepted

$ curl -sS https://quotes-web-day19.../api/events/digest
[{"...","workerId":"digest-worker-2", ...}]   # recorded, through the full MI→Entra→real-namespace path

$ curl -sS -X POST https://quotes-web-day19.../api/events/publish-poison
$ curl -sS https://quotes-web-day19.../api/events/dead-letters
[{"subscription":"digest-notifications","deadLetterReason":"MaxDeliveryCountExceeded",...},
 {"subscription":"audit-log","deadLetterReason":"MaxDeliveryCountExceeded",...}]
```

**A resilience bug found and fixed while deploying**: `DeadLetterMonitorWorker`'s
polling loop only caught `OperationCanceledException` around its dead-letter
drain calls. The first deploy hit a real (if transient) `UnauthorizedAccessException`
— the new role assignment for the Managed Identity hadn't propagated to
Service Bus's data plane yet — and because an unhandled exception from a
`BackgroundService.ExecuteAsync` stops the **entire host** by default, that
one transient error crash-looped the whole API, not just the dead-letter
poll. Fixed by catching `Exception` around the per-iteration drain calls and
logging + retrying next poll instead — shipped as `quotes-api-day19:0.1.1`.
This applies locally too, not just in Azure; it just never surfaced against
the emulator because RBAC propagation delay doesn't exist there.

**Real, billable Azure resources were created for this** — not covered by
any free tier: a Service Bus **Standard** namespace (no Basic-tier topics)
and two Container Apps kept at `--min-replicas 1` (needed for the
background consumers to stay running; scale-to-zero would kill them between
requests). To tear it down when done:

```bash
az group show -n thinkschool-rg  # confirm you're targeting the right group
az containerapp delete -g thinkschool-rg -n quotes-web-day19 --yes
az containerapp delete -g thinkschool-rg -n quotes-api-day19 --yes
az servicebus namespace delete -g thinkschool-rg -n sb-thinkschool-day19
```

(Deliberately left running rather than torn down automatically after
verification, so the link above stays live — delete at your discretion.)

## The feature

`POST /api/events/quote-created` publishes a `QuoteCreated` event to the
`quotes.events` topic. The topic fans that one message out to **two
subscriptions**:

| Subscription | Consumers | Purpose |
|---|---|---|
| `audit-log` | 1 worker (`AuditLogSubscriptionWorker`) | An always-on audit trail — every event, recorded once. |
| `digest-notifications` | 3 competing workers (`DigestConsumerPool`) | Simulates a scaled-out notification service — Service Bus hands each message to whichever of the three workers asks for it first. |

```
POST /api/events/quote-created {"quoteId":1,"author":"Ada Lovelace","text":"..."}
  -> 202 Accepted {"eventId": "...", ...}       <-- fans out to both subscriptions
GET /api/events/audit-log        -> rows written by the one audit-log worker
GET /api/events/digest           -> rows written by the three competing digest workers, each tagged with its workerId
GET /api/events/dead-letters      -> poison messages Service Bus moved to a DLQ
```

The Angular app's **Service Bus Events** panel (bottom-center) drives this
directly: publish an event, watch it land in both subscriptions' lists in
real time, tick "simulate a crash" to watch a redelivery get deduped, or
publish a poison message and watch it get dead-lettered.

## Idempotency: dedupe on message id

Every handler (both `AuditLogSubscriptionWorker` and each of the three
`DigestConsumerPool` workers) writes its side effect and a
`ProcessedMessage(Subscription, MessageId)` row in the **same
`SaveChangesAsync` call** (`MessageIdempotency.TryProcessOnceAsync`). A
unique index on `(Subscription, MessageId)` is what makes this airtight: if
that exact pair was already committed by an earlier delivery, the second
insert throws `DbUpdateException` and the handler treats it as a duplicate
— no re-inserted row, no double-processing — instead of trusting delivery
count or a fragile "have I seen this before" check with a race window.

This isn't a simulated resend: publishing with `simulateCrash: true` makes
every subscription's handler commit its side effect on delivery #1 and
then throw *after* the commit but *before* the message would be
acknowledged — the same shape as a real process crash between "did the
work" and "told the broker." Service Bus's own `AutoCompleteMessages`
abandons the message on that unhandled exception and redelivers it. The
second delivery is genuinely a new `ProcessMessageEventArgs` call, and for
`digest-notifications` it can land on a **different** competing worker than
the one that saw delivery #1 — which is exactly what happened in the
captured run below:

```
[10:45:07 INF] digest-worker-1: recorded QuoteCreated for quote 3 (message f7ffeef6-..., delivery #1).
[10:45:07 WRN] digest-worker-1: simulating a crash after committing message f7ffeef6-... but before acknowledging it — expect a redelivery next, possibly to a different worker.
[10:45:07 INF] digest-worker-3: duplicate delivery #2 of message f7ffeef6-... — already recorded, skipping.
```

`digest-worker-1` committed the row; the redelivery was picked up by
`digest-worker-3`, which correctly recognized the (subscription, message
id) pair as already claimed and skipped it. The full run, including the
same thing happening on `audit-log`, is in
`results/servicebus-demo.log`.

## Competing consumers

`DigestConsumerPool` starts `ServiceBusOptions.DigestWorkerCount` (3)
independent `ServiceBusProcessor` instances, all bound to the same
`digest-notifications` subscription — not one processor with
`MaxConcurrentCalls: 3`, but three genuinely separate consumer instances,
the way three replicas of a notification service would look in production.
Service Bus hands each message to exactly one of them. Across three
published events, the captured run shows three different workers picking
up work:

```
digest-worker-3: recorded QuoteCreated for quote 1 (message 990c8133-..., delivery #1).
digest-worker-2: recorded QuoteCreated for quote 2 (message ac5b7fba-..., delivery #1).
digest-worker-1: recorded QuoteCreated for quote 3 (message f7ffeef6-..., delivery #1).
```

Contrast with `AuditLogSubscriptionWorker`, a single always-on consumer on
`audit-log` — not competing, by design, since an audit trail wants exactly
one writer.

## Dead-letter queue: catching a poison message

`POST /api/events/publish-poison` sends a message whose body is not valid
`QuoteEventMessage` JSON. Every subscription's handler calls
`JsonSerializer.Deserialize<QuoteEventMessage>` before it does anything
else, so it throws on **every** delivery attempt, on **both**
subscriptions. With `AutoCompleteMessages: true` (the default), Service Bus
abandons the message after each failed attempt and redelivers it; once each
subscription's `MaxDeliveryCount` (3, set in
`servicebus-emulator/Config/Config.json`) is exhausted, Service Bus itself
— not application code — moves that subscription's copy to its dead-letter
subqueue.

`DeadLetterMonitorWorker` polls both subscriptions' `$deadletterqueue` (via
`ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter }`) every 2
seconds, and turns whatever it finds into a durable, queryable
`DeadLetteredMessage` row before completing it out of the DLQ. Captured
live:

```
[10:45:10 WRN] Published a deliberately malformed poison message c9c6f917-... to topic 'quotes.events'. Every subscription's handler will fail to deserialize it on every attempt.
[10:45:10 WRN] Dead-letter monitor: subscription 'digest-notifications' dead-lettered message c9c6f917-... after 4 delivery attempt(s) — reason: MaxDeliveryCountExceeded (Message could not be consumed after 3 delivery attempts.).
[10:45:13 WRN] Dead-letter monitor: subscription 'audit-log' dead-lettered message c9c6f917-... after 4 delivery attempt(s) — reason: MaxDeliveryCountExceeded (Message could not be consumed after 3 delivery attempts.).
```

One detail confirmed live rather than assumed: the receiver's own
`DeliveryCount` reads **4** at the moment it's found in the DLQ even though
the reason text says "after 3 delivery attempts" — the emulator increments
the counter once more when it moves the message into the dead-letter
subqueue itself. Both subscriptions dead-lettered their own independent
copy, proving the poison message was caught on both fan-out paths, not
just one.

## Why the Service Bus emulator, not a fake

The `Azure.Messaging.ServiceBus` SDK talks to a real broker over AMQP;
there's no meaningful in-memory substitute for topic fan-out, competing
consumers actually racing over the network, or `MaxDeliveryCount`-driven
dead-lettering — those are broker behaviors, not application code. Rather
than mock any of that, this uses Microsoft's official
[Service Bus emulator](https://github.com/Azure/azure-service-bus-emulator-installer)
(`mcr.microsoft.com/azure-messaging/servicebus-emulator`, backed by
Azure SQL Edge), run locally via `docker compose`. The topic and its two
subscriptions are declared in `servicebus-emulator/Config/Config.json` and
provisioned automatically when the emulator container starts — no
`ServiceBusAdministrationClient` calls needed at runtime.

One config gotcha found and fixed while wiring this up: the emulator
rejects `DuplicateDetectionHistoryTimeWindow` values over 5 minutes
(`Expected time to be less than or equal to 5m ... but found 10m`) and
fails to start with no other symptom — `Config.json` here uses `PT5M`.

## Running it locally

```bash
# Terminal 1 — the Service Bus emulator (topic + both subscriptions provisioned from Config.json)
cd Day-19
docker compose up -d
# wait for it to report healthy:
curl http://localhost:5300/health   # 200 once ready (can take ~30-60s)

# Terminal 2 — backend
cd Day-19/QuotesApi
dotnet run --urls http://localhost:5147

# Terminal 3 — frontend
cd Day-19/quotes-angular
npm install
npm start   # ng serve, proxies /api to :5147 (see proxy.conf.json)
# http://localhost:4200 — sign in, then use the "Service Bus Events" panel
```

To see it yourself: publish a normal event and watch both the audit log
and digest columns fill in with different `digest-worker-*` tags across a
few publishes; tick "simulate a crash" and publish again to watch the
redelivery get deduped (only one row per message, but check the backend
console for the "duplicate delivery #2 ... skipping" line); publish a
poison message and wait ~10s for it to show up under "Dead letters" on
both subscriptions.

## What was copied, and why

Per the brief: nothing outside `Day-19/` was modified. Everything Day-19
depends on was **copied into** `Day-19/` first, then adapted here:

- `Day-19/QuotesApi/` — copied from `Day-18/QuotesApi` (EF Core + Sqlite,
  JWT auth, Serilog, OpenTelemetry, the background-jobs queue, Hangfire).
  Added: `Messaging/` (the publisher, the idempotency helper, both
  subscription workers, the dead-letter monitor),
  `Extensions/ServiceBusEndpointExtensions.cs`, four new `Models/*` (
  `ProcessedMessage`, `AuditLogEntry`, `DigestNotification`,
  `DeadLetteredMessage`) plus the `AddServiceBusEvents` EF Core migration,
  and the `ServiceBus` section in `appsettings.json` / wiring in
  `Program.cs` / `InfrastructureExtensions.cs`.
- `Day-19/quotes-angular/` — copied from `Day-18/quotes-angular` (routing,
  guards, the signals-based store, the API-activity inspector, the
  background-jobs panel). Added: `service-bus-events-panel/` (component +
  template + styles), `services/service-bus-events-api.ts`,
  `models/service-bus-event.model.ts`, and wired the new panel into
  `app.ts`/`app.html` alongside the existing two panels.
- `Day-19/servicebus-emulator/Config/Config.json` and
  `Day-19/docker-compose.yml` — new for Day 19, declaring the
  `quotes.events` topic and its two subscriptions and running the
  emulator + its Azure SQL Edge dependency locally.

**The dev bearer token was reused, not regenerated**: Day-19 kept the same
`UserSecretsId` as Day-18 (itself copied unchanged from Day-5), so it
shares the same local `Jwt:Key` user secret. `dev-config/dev-token.local.json`
here is a freshly minted token (new `email`/subject, 2-year expiry) signed
with that same shared key — same issuer/audience/scope the API expects,
independently generated rather than copying Day-18's token file verbatim.

## Screenshots (`screenshots/`)

- `day19-01-signed-in.png` — signed in, all three panels available
- `day19-02-panel-empty.png` — Service Bus Events panel opened, nothing published yet
- `day19-03-fanout-audit-and-digest.png` — one event published, recorded on both `audit-log` and `digest-notifications`
- `day19-04-competing-consumers.png` — a second event, picked up by a different `digest-worker-*`
- `day19-05-idempotent-dedupe.png` — a third event published with "simulate a crash" ticked; only one row per message despite the redelivery (see `results/servicebus-demo.log` for the actual duplicate-delivery log line)
- `day19-06-poison-published.png` — poison message just published, not yet dead-lettered
- `day19-07-dead-lettered.png` — both subscriptions' copies now show up under "Dead letters" with reason `MaxDeliveryCountExceeded`

## Files added

```
Day-19/docker-compose.yml
Day-19/servicebus-emulator/Config/Config.json

Day-19/QuotesApi/Messaging/
  ServiceBusOptions.cs            connection string, topic/subscription names, digest worker count
  QuoteEventMessage.cs            the published event's JSON shape
  IQuoteEventPublisher.cs / QuoteEventPublisher.cs   publishes normal + poison messages
  MessageIdempotency.cs           the (subscription, MessageId) dedupe helper
  AuditLogSubscriptionWorker.cs   single consumer of "audit-log"
  DigestConsumerPool.cs           3 competing consumers of "digest-notifications"
  DeadLetterMonitorWorker.cs      polls both subscriptions' dead-letter subqueues
Day-19/QuotesApi/Models/
  ProcessedMessage.cs / AuditLogEntry.cs / DigestNotification.cs / DeadLetteredMessage.cs
Day-19/QuotesApi/Extensions/ServiceBusEndpointExtensions.cs
  POST /api/events/quote-created, POST /api/events/publish-poison,
  GET /api/events/audit-log, GET /api/events/digest, GET /api/events/dead-letters
Day-19/QuotesApi/Migrations/*_AddServiceBusEvents*

Day-19/quotes-angular/src/app/service-bus-events-panel/
  service-bus-events-panel.ts/.html/.css
Day-19/quotes-angular/src/app/services/service-bus-events-api.ts
Day-19/quotes-angular/src/app/models/service-bus-event.model.ts
```
