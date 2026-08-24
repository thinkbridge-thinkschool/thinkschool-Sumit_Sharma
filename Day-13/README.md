# Day 13 — Signals + Zoneless + Standalone

## Real API

The Angular app consumes the real Week-1 API from `Day-5/QuotesApi`.

- Endpoint: `GET /api/quotes`
- Query params: `page` (default 1), `size` (default 10, capped at 100)
- Defined in `Day-5/QuotesApi/Extensions/QuoteEndpointExtensions.cs`
- Response: JSON array of quote objects (no auth required for this endpoint)

Response fields used (from `Day-5/QuotesApi/Models/Quote.cs`, as serialized by the running API):

```json
[
  {
    "id": 1,
    "author": "Ada Lovelace",
    "text": "That brain of mine is something more than merely mortal.",
    "isDeleted": false
  }
]
```

`id`, `author`, `text`, and `isDeleted` are the exact camelCase fields returned by the live endpoint — nothing was invented.

No API code was modified. The only integration concession is a dev-server proxy (`Day-13/quotes-angular/proxy.conf.json`), which forwards `/api/*` requests from the Angular dev server to `http://localhost:5147`. This avoids any need for CORS configuration on the Week-1 API, so Day-5 stays untouched.

## Architecture

```
Day-5 QuotesApi (GET /api/quotes)
  → QuotesApi service (src/app/services/quotes-api.ts, HttpClient via inject())
  → signals (quotes, loading, error, authorFilter) in src/app/quotes/quotes.ts
  → computed (authors, filteredQuotes, quoteCount)
  → template (src/app/quotes/quotes.html)
```

```
authorFilter signal changes
  → effect() recomputes document.title and logs to console
```

## Signals

Defined in `src/app/quotes/quotes.ts`:

- `quotes: signal<Quote[]>([])` — the raw list fetched from the API
- `loading: signal<boolean>` — true while the HTTP request is in flight
- `error: signal<string | null>` — set if the request fails
- `authorFilter: signal<string>('all')` — the currently selected author in the dropdown
- `lastEffectRun: signal<string | null>` — the last document-title value written by the effect, surfaced in the UI so the effect's output is visible without opening devtools

## computed()

- `authors = computed(...)` — derives the sorted, deduplicated list of author names (plus `'all'`) from `quotes()`, used to populate the filter dropdown.
- `filteredQuotes = computed(...)` — derives the quotes to render by filtering `quotes()` against `authorFilter()`.
- `quoteCount = computed(() => filteredQuotes().length)` — the count actually displayed to the user. It's computed rather than stored because it's fully derived from `filteredQuotes`, which is itself derived from two other signals — storing it separately would require manually keeping it in sync on every quotes/filter change.

## effect()

```ts
effect(() => {
  const author = this.authorFilter();
  const count = this.quoteCount();
  const label = author === 'all' ? `Quotes (${count})` : `Quotes — ${author} (${count})`;
  document.title = label;
  console.log(`[effect] author filter changed to "${author}" — ${count} quote(s) visible`);
  this.lastEffectRun.set(label);
});
```

It reacts to `authorFilter` (and transitively to `quotes`, via `quoteCount`) and performs a real side effect — writing `document.title` — plus a console log. It is not a stand-in for `computed()`: nothing it does is read back into another signal's derivation except the display-only `lastEffectRun`, which exists purely so the verification below doesn't require devtools.

## Zoneless

Configured in `src/app/app.config.ts` via `provideZonelessChangeDetection()`. The app was also generated with `ng new --zoneless`, so `zone.js` is not in `package.json` dependencies and there is no zone.js polyfill in `angular.json`. This exercise uses zoneless mode because Angular's signal-based reactivity is designed to replace zone.js's dirty-checking — signals notify the framework directly on write, so a zone patching every async API is unnecessary.

## Standalone

`src/app/app.ts` bootstraps with `bootstrapApplication(App, appConfig)` in `src/main.ts`. Every component (`App`, `Quotes`) is a standalone `@Component` with an `imports` array. There is no `AppModule`, `NgModule`, or `declarations` array anywhere in `src/`.

## Dependency injection

`inject()` is used everywhere DI is needed, with no constructor-parameter injection:

- `src/app/services/quotes-api.ts`: `private readonly http = inject(HttpClient);`
- `src/app/quotes/quotes.ts`: `private readonly quotesApi = inject(QuotesApi);`

The only `constructor()` in the app (`quotes.ts`) takes no parameters — it exists solely to register `effect()` in an injection context and kick off the initial load.

## Verification

All verification below was performed against the actually-running apps (Day-5 API + Day-13 Angular dev server), not fabricated.

**Build**

```
npm run build
```
succeeded: `Application bundle generation complete.` — output in `dist/quotes-angular`.

**Backend**

`Day-5/QuotesApi` was started with `dotnet run` (with a local `Jwt__Key` env var supplied at runtime only, since the app fails startup without one — no file was changed). `GET /health` returned `Healthy`. Five real quote rows (`Ada Lovelace`, `Alan Turing`, `Grace Hopper`, `Barbara Liskov`) were inserted directly into the API's own SQLite database so `GET /api/quotes` had real data to serve; this is data through the real repository/endpoint, not a mocked response. These rows and the local `quotes.db` were removed again after verification so `Day-5` is back to its original state.

**Frontend + integration**

The dev server was started with `ng serve --port 4213` (using `proxy.conf.json`), and `Day-13` was headlessly loaded in a real Chromium instance (installed via `puppeteer browsers install chrome`, used only as a throwaway verification tool outside the project). Captured results:

- Network: the browser issued `GET http://localhost:4213/api/quotes?page=1&size=50` → `200`, proxied straight through to the real `Day-5` API.
- `zoneJsPresent` (`typeof window.Zone !== 'undefined'`): `false` — confirms zoneless is actually active, not just configured.
- Initial render: `document.title` = `"Quotes (5)"`, summary = `"Showing 5 of 5 quote(s)"`, 5 quote cards rendered with the real author/text values from the seeded rows.
- Author dropdown options: `["all", "Ada Lovelace", "Alan Turing", "Barbara Liskov", "Grace Hopper"]` — the `authors` computed value.
- After selecting `"Ada Lovelace"` in the filter: `document.title` = `"Quotes — Ada Lovelace (2)"`, summary = `"Showing 2 of 5 quote(s)"`, and the two rendered quotes were exactly Ada Lovelace's two — confirming `filteredQuotes` and `quoteCount` computed correctly.
- Console log lines captured from the page during this run:
  ```
  [effect] author filter changed to "all" — 0 quote(s) visible
  [effect] author filter changed to "all" — 5 quote(s) visible
  [effect] author filter changed to "Ada Lovelace" — 2 quote(s) visible
  ```
  The first line fires on component construction (before the HTTP response resolves), the second fires once `quotes` is set from the real API response, and the third fires after the dropdown selection — demonstrating `effect()` re-running each time its signal dependencies change.

## What changed

Older Angular relied on zone.js patching every async browser API (timers, DOM events, fetch/XHR) and re-running change detection on the whole tree whenever any of them fired, with no way to know which piece of state actually changed. Here, `quotes`, `authorFilter`, and the derived `computed()` values notify the framework directly when they're written, so only what depends on them updates, and there's no zone.js patch layer at all (`window.Zone` is undefined, as verified above). The state flow — API → signal → computed → template, with an `effect()` on the side — is easy to read top-to-bottom in `quotes.ts` because every dependency is explicit at the point it's read, rather than implied by whatever zone.js happened to intercept.
