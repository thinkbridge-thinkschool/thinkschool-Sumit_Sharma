# Day 16 — Routing/Guards (Task 1) and Signal State Management (Task 2)

## Task 1: Routing, Lazy Loading, Guards

Copies the Day-15 Angular app (`quotes-angular`, copied into `Day-16/quotes-angular` —
Day-1 through Day-15 were not modified) and replaces its tab-based/manual
navigation with real Angular routing: a real `/quotes` and `/quotes/:id`, a
lazy-loaded detail feature, a functional auth guard reusing Day-15's existing
dev-token auth, and Angular View Transitions — verified against the real,
running Day-5 `QuotesApi`.

## Brief

1. Real routes for `/quotes` (list) and `/quotes/:id` (detail), navigated by
   clicking a quote, reading the real id from the route param.
2. The detail feature must be lazy-loaded — not present in the initial bundle.
3. A functional route guard, reusing Day-15's existing dev-token auth
   mechanism (no second auth system), redirecting unauthenticated users away
   from a protected route.
4. Angular View Transitions enabled for navigation, subtle/professional.
5. Keep the existing design; replace manual tab navigation with real routing.
6. Actually run the real Day-5 API and the real app and verify everything.
7. Catch and fix one real bug/wrong assumption from actually testing it.
8. `ng test` and `ng build` pass, with new tests for routing/guard behavior.

## Root cause of the "list still shows at /quotes/:id" report (not an app bug)

This was reported **twice**, both times against `http://localhost:4200/quotes/1`
showing the URL change but the quotes list staying on screen instead of
`QuoteDetail`. Both times it was investigated as a real routing bug per
requirement 7 — full source-level audit of `app.html`/`app.ts`/`app.routes.ts`/
`app.config.ts`/`quotes.ts`, live Playwright runs, network inspection — and
both times the Day-16 code itself was already correct. The actual cause:

**`ng serve` defaults to port 4200 in every day's Angular app**, including
both `Day-15/quotes-angular` and `Day-16/quotes-angular`. Day-15 has **no**
Angular Router at all — no `provideRouter`, no `<router-outlet>` (confirmed
by grepping its `app.config.ts`/`app.ts`/`app.html` — nothing matches). Both
times this was reported, `localhost:4200` was independently confirmed (by
process inspection: `ss -ltnp`, the listening pid's `cwd`, and by fetching
`main.js` and checking for the literal string `provideRouter`) to be serving
**Day-15's** dev server, not Day-16's. Visiting `http://localhost:4200/quotes/1`
against that server:

- updates the browser's own address bar to `/quotes/1` (that's just the
  browser doing what any URL bar does — it doesn't require the app to have
  a router), while
- the dev server's SPA fallback serves the same `index.html` it always
  does, booting the *routerless* Day-15 app, which has no idea `/quotes/1`
  means anything and just renders its default tab-based view (the quotes
  list) — exactly the symptom reported.

The first time, the recommended fix was to run Day-16 on a different port.
That didn't resolve the confusion, so the second time this was resolved
**directly**: Day-15's dev server (pid `16060`, cwd `Day-15/quotes-angular`)
was stopped, and Day-16's dev server was started on port 4200 itself —
confirmed via `curl http://localhost:4200/main.js | grep provideRouter`
before re-testing. With Day-16 actually serving `:4200`, the exact reported
scenario now passes, live, at that exact URL:

1. `/quotes` renders the list — 28 real quote cards.
2. Clicking a quote → URL becomes `/quotes/1`.
3. `QuoteDetail` replaces the list — `.quote-grid` count `0`, `.detail-card`
   count `1`, showing `Quote #1` / `Ada Lovelace`.
4. Network shows the real `200 GET http://localhost:4200/api/quotes/1`.
5. A fresh, direct `page.goto('http://localhost:4200/quotes/1')` (not an SPA
   click — a brand-new navigation, matching a typed-URL / bookmark scenario)
   also renders the detail component correctly, not the list.
6. Refreshing `/quotes/1` re-renders the same detail correctly.
7. Detail route stays lazy — the only new `.js` chunk fetched on the
   list→detail navigation was the `quote-detail` chunk.
8. Screenshot re-captured live from this exact run:
   `Day-16/screenshots/day16-task1-detail.png`.

`ng test`: 8 files, 36/36 passing (re-run, unchanged). `ng build`: clean,
lazy chunks unchanged (re-run, unchanged). **No app code changed** in either
round of this investigation — routing, the guard, and lazy-loading were
already correct both times; what changed was which dev server was actually
listening on `:4200`.

If you stop Day-16's server later and want Day-15 back on `:4200`:
`cd Day-15/quotes-angular && ng serve`. To run both at once, give Day-16 an
explicit different port: `cd Day-16/quotes-angular && npx ng serve --port 4210`.

## Routes created

`src/app/app.routes.ts`:

```ts
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'quotes' },
  { path: 'quotes', component: Quotes, title: 'Quotes' },
  {
    path: 'quotes/:id',
    loadComponent: () => import('./quote-detail/quote-detail').then((m) => m.QuoteDetail),
    title: 'Quote detail',
  },
  {
    path: 'create',
    loadComponent: () => import('./create-quote/create-quote').then((m) => m.CreateQuote),
    canActivate: [authGuard],
    title: 'Create a quote',
  },
  { path: '**', redirectTo: 'quotes' },
];
```

- `/quotes` — the list page (formerly the "Browse" tab). Eagerly loaded
  (statically `component:`-referenced) since it's the landing route; each
  card is a real `[routerLink]="['/quotes', quote.id]"`, not a click handler
  that swaps a signal.
- `/quotes/:id` — the detail page (formerly the inline "Explorer" click-to-detail
  view). Reads the id from the real route param via `ActivatedRoute.paramMap`.
- `/create` — the existing create-quote form, now a real guarded route instead
  of a tab, since the backend's `POST /api/quotes` already requires the
  `CanEditQuotes` policy — routing was the natural place to demonstrate the
  guard against something the API genuinely protects.
- `''` and `'**'` both redirect to `/quotes`.

**Scope decision:** the old "Explorer" tab (an inline click-to-select detail
view) and "Create (Signal Forms)" tab were dropped rather than ported to
routes. Explorer's job is now done properly by `/quotes` → `/quotes/:id`;
Signal Forms wasn't part of this task's routing/guard/lazy-loading scope, and
leaving it unrouted would have meant dead, unreachable code.

## Lazy loading

`quote-detail` and `create-quote` are both loaded via `loadComponent: () =>
import(...)`, not statically imported anywhere else in the eagerly-loaded
graph. Confirmed with real `ng build` output — both are separate **Lazy chunk
files**, absent from the **Initial chunk files**:

```
Initial chunk files | Names         |  Raw size | Estimated transfer size
chunk-WRTK5VZS.js   | -             | 258.24 kB |                70.29 kB
main-MRS6AP2B.js    | main          |  26.81 kB |                 6.95 kB
styles-KQJKIRQ6.css | styles        |    977 B  |                  977 B
chunk-SU2ZOHJH.js   | -             |    673 B  |                  673 B
                    | Initial total | 286.70 kB |                78.88 kB

Lazy chunk files    | Names         |  Raw size | Estimated transfer size
chunk-55F4A7I7.js   | create-quote  |  43.63 kB |                 9.97 kB
chunk-TWIXDMUH.js   | quote-detail  |   5.88 kB |                 1.85 kB
```

Confirmed again live, in a real browser (Playwright), watching network
traffic: loading `/quotes` never requests the `quote-detail` chunk; clicking
a quote card triggers exactly one new `.js` request (`chunk-*.js` for
`quote-detail`) at the moment of navigation.

## Functional auth guard

`src/app/auth/auth.guard.ts`:

```ts
export const authGuard: CanActivateFn = () => {
  if (getDevBearerToken()) {
    return true;
  }
  const router = inject(Router);
  return router.createUrlTree(['/quotes'], { queryParams: { authRequired: 'true' } });
};
```

Reuses Day-15's existing dev-token mechanism exactly as-is —
`getDevBearerToken()` from `auth/dev-token.ts`, the same `localStorage`
value the real `authInterceptor` already attaches as `Authorization: Bearer
<token>` on `/api/*` requests. No second auth system, no token value ever
read into a route param, query string, or log. Applied to `/create` via
`canActivate: [authGuard]`. On an unauthenticated attempt, it redirects to
the existing `/quotes` route (not a new page) with `?authRequired=true`,
which the quotes page reads to show a dismissible "Sign in to create a
quote." banner.

## View Transition

`app.config.ts`: `provideRouter(routes, withViewTransitions())`.

`app.css` gives it a subtle, professional feel — and this is also where the
one real bug below was found and fixed (see "Bug caught and fixed").

## Real API endpoints/fields used

```
GET /api/quotes?page=1&size=50   → list (unchanged from Day-15)
GET /api/quotes/{id}             → detail (new: QuotesApi.getQuoteById(id))
POST /api/quotes                 → create (unchanged from Day-15, now behind the guard)
```

Quote fields used, exactly as returned by the real API — nothing invented:
`{ id: number, author: string, text: string, isDeleted: boolean }`.

## Verification (real API + real app, actually run)

Environment note: like Day-15, this sandbox has no `Jwt:Key` configured
anywhere. A random local-only signing key was generated, kept outside the
repo, never printed or committed, and passed to the API via the `Jwt__Key`
environment variable. A token was minted locally with the same claims shape
the API's own `GetQuoteByIdEndpointTests` test class uses (`scope:
quotes.write`, matching `Jwt:Issuer`/`Jwt:Audience` from `appsettings.json`)
and written to the gitignored `dev-config/dev-token.local.json` Day-15's
`loadDevTokenFromLocalConfig` already reads at app-init. No real/production
token or key appears anywhere in the diff.

Commands run:

```bash
# Terminal 1 — real API
cd Day-5/QuotesApi
Jwt__Key=<local-only-generated-secret> dotnet run --urls http://localhost:5147

# Terminal 2 — real app
cd Day-16/quotes-angular
npm install
npx ng test --watch=false     # 8 files, 36/36 passing
npx ng build                  # production bundle, lazy chunks confirmed above
npx ng serve --port 4202      # dev server, proxies /api to :5147
```

Driven with a real headless-browser session (Playwright) against the live,
running app:

- **`/quotes` loads the real quote list** — 27 real quotes rendered from a
  genuine `GET /api/quotes?page=1&size=50`.
- **Clicking a real quote navigates to `/quotes/:id`** — clicked the first
  card (`href="/quotes/1"`), landed on `http://localhost:4202/quotes/1`.
- **The detail request uses the real id and the real endpoint** — network
  log showed exactly `200 GET /api/quotes/1`; page showed `Quote #1` / `Ada
  Lovelace`, matching the API response.
- **Refreshing `/quotes/:id` still works** — hard reload on `/quotes/1`
  re-fetched and re-rendered the same quote.
- **Detail code is lazy-loaded** — confirmed via both `ng build` output and
  live network watching (above).
- **Authenticated access works** — with the dev token present, visited
  `/create` (guard passed), filled in the real form, submitted → real `201
  POST /api/quotes` → `Quote #28 by Day16 UI Verifier`, then followed the
  "View it" link → landed on `/quotes/28` showing the same quote via a real
  `GET /api/quotes/28`.
- **Unauthenticated access is redirected by the guard** — see "Bug caught
  and fixed" below; confirmed correct in both an in-app navigation with the
  token cleared, and a genuinely unauthenticated production build.
- **A missing/invalid quote id is handled cleanly** — `/quotes/999999`
  (valid but nonexistent, real `404` from the API) and `/quotes/not-a-number`
  (never even calls the API — caught client-side) both render the same
  "Quote not found" state with a link back to the list. No crash, no raw
  JSON.
- **Browser back navigation works** — after list → detail, browser back
  returned to `/quotes` with all 27 quotes still rendered.
- **View Transition is enabled and navigation still works regardless** —
  confirmed the transition actually runs (see below); confirmed normal
  navigation isn't blocked if a browser doesn't support
  `document.startViewTransition` (Angular's `withViewTransitions()` falls
  back to a plain instant navigation automatically — nothing in this app's
  code depends on the transition completing).

`ng test`: **8 test files, 36/36 passing** (24 pre-existing Day-15 tests,
unmodified and still green, plus 12 new: `auth.guard.spec.ts`,
`app.routes.spec.ts`, `quote-detail.spec.ts`). `ng build`: clean.

## Bug caught and fixed

**Assumption:** enabling `withViewTransitions()` and adding
`::view-transition-old(root)` / `::view-transition-new(root)` with a plain
`animation-duration` would give a "subtle" cross-fade between any two
routed pages.

**Reality (found by actually screenshotting the live transition):** the
first list → detail screenshot, taken right after `page.waitForSelector`
resolved (i.e. the moment the new DOM was confirmed present), came back
visibly broken — the list's hero title, stat counters ("27 / 17 / 27"), and
quote grid were all overlapping the detail card's "QUOTE #1" and quote text
in one illegible, double-exposed frame. The default browser cross-dissolve
renders **both** the old and new root snapshots superimposed, blending
opacity, for the entire animation duration. That's fine when a transition
is between two similar layouts (e.g. a shared element moving), but `/quotes`
(a multi-column grid) and `/quotes/:id` (a single centered card) share
nothing spatially, so blending them produces genuinely illegible overlapping
text — not just in a screenshot, but for a real person watching the
transition live, for the whole ~180ms window.

**Fix:** replaced the simultaneous cross-dissolve with a sequential
fade-through in `app.css` — the old view fades out first (0–90ms), *then*
the new view fades in (90–200ms, via `animation-delay`), so only one page is
ever visibly on screen at a time:

```css
::view-transition-old(root) {
  animation: 90ms ease-out both view-transition-fade-out;
}
::view-transition-new(root) {
  animation: 110ms ease-in 90ms both view-transition-fade-in;
}
```

Re-verified: a screenshot taken at the same "DOM just landed" instant now
shows a clean, still-mostly-old-page frame (no overlap), and the settled
post-transition screenshot (`day16-task1-detail.png`) is clean. The
transition is real (confirmed via `document.getAnimations()` actually
running during navigation) and reads as a quick, professional fade rather
than a blend of two unrelated layouts.

**Secondary finding, also worth documenting:** the *first* attempt to
demonstrate the guard used `page.goto('/create')` after clearing the dev
token from `localStorage`, expecting to land back on `/quotes` — instead it
loaded `/create` successfully. This looked like "guard not actually
protecting the route." Investigating further: a full page load re-runs
Day-15's existing `loadDevTokenFromLocalConfig` app initializer, which
*unconditionally* re-fetches and re-writes a fresh dev token into
`localStorage` in dev mode, before the router's initial navigation (and
therefore the guard) ever runs — so a full reload of a dev-mode session can
never observe "logged out." The guard itself was never broken: confirmed by
(a) an in-app `routerLink` navigation to `/create` with the token cleared,
with no reload in between, which correctly redirected to
`/quotes?authRequired=true` and showed the banner; and (b) a genuine
production-mode build (`ng serve --configuration production`, where
`dev-config/dev-token.local.json` isn't even served and `loadDevTokenFromLocalConfig`
is a no-op), where a fresh, cookie-less browser context landing directly on
`/create` was correctly redirected. `day16-task1-guard.png` was captured
from that production-mode run. No code change was needed here — same as a
precedent already in this codebase (Day-15's README documents an assumption
disproven by live testing where the fix was to the test plan, not the app)
— but it's a real trap worth flagging for anyone else testing this guard in
dev mode: **use an in-app navigation or a production build, not a dev-mode
full reload, to observe the "logged out" redirect.**

## What would break if the real API contract changed

- **`GET /api/quotes/{id}` renamed/removed a field** (e.g. `text` →
  `content`): nothing currently validates the response shape at runtime
  beyond TypeScript's compile-time `Quote` interface, so `quote-detail.html`
  would silently render `undefined` for that field instead of erroring —
  the characterization-style safety net that would catch this is a
  dedicated shape-asserting test against the live API, which this task's
  scope didn't add for the single-quote endpoint (only for the list, in
  `characterization/quotes-api.characterization.spec.ts`, inherited from
  Day-15).
- **`{id:int}` route constraint changed to a non-numeric key** (e.g. a
  GUID): `QuoteDetail`'s `!Number.isInteger(id)` guard would treat every
  real id as invalid and show "Quote not found" for every quote — this is
  the direct risk of the client re-deriving a type assumption (`id: number`)
  instead of trusting whatever the server actually returns as an opaque
  route param.
- **`GET /api/quotes/{id}` stopped 404-ing bare** (e.g. wrapped every error
  in `ProblemDetails`): already handled — `error-mapping.interceptor.ts`'s
  404 branch reads `body?.detail ?? body?.title ?? '<fallback>'`, tolerating
  either an empty or a structured body.
- **`GET /api/quotes` response shape changed** (list → enveloped object):
  same risk Day-15 already documented — `QuotesApi.getQuotes()`'s
  `Observable<Quote[]>` typing wouldn't match reality, and `/quotes`'s
  `.map`/`.filter` calls would throw at runtime instead of failing at
  compile time.
- **`POST /api/quotes`'s `CanEditQuotes` policy changed** (e.g. a different
  claim/scope required): the Angular guard is unaffected either way — it
  only checks "is a dev token present," not what's inside it — but a
  genuinely authenticated user could then still get a real `401`/`403` from
  the live API on submit, which `error-mapping.interceptor.ts` already
  turns into "You are not authorized to perform this action." rather than a
  crash.

## Screenshots

- `Day-16/screenshots/day16-task1-list.png` — `/quotes`, real data, 28
  quotes, API-connected status.
- `Day-16/screenshots/day16-task1-detail.png` — `/quotes/1`, real quote,
  showing author, text, id, and the back link.
- `Day-16/screenshots/day16-task1-guard.png` — a genuinely unauthenticated
  session redirected by the guard from `/create` to
  `/quotes?authRequired=true`, showing the "Sign in to create a quote."
  banner.

No JWT/token value appears in any screenshot, the DOM, or this document.

---

## Task 2: State Management, Signals First

### Brief

1. A signal-based state layer for the Quotes feature (quotes, loading,
   error, filter) — no NgRx unless there's a real reason.
2. `computed()` for anything derivable — filtered quotes, quote count,
   author count, empty-state — never duplicated as separate signals.
3. Real API only: `GET /api/quotes?page=1&size=50`, real `Quote` fields,
   through the existing `QuotesApi` service and interceptor chain.
4. Components stay presentational; the store owns feature state and
   API-loading state.
5. Exercise the real app against the real API: loading, success, empty,
   error, filter changes, computed counts, concurrent refresh.
6. Justify signals-over-NgRx in this README, without claiming NgRx is
   universally worse.
7. Catch and fix one real bug.
8. Tests for the store and the component; `npm test -- --watch=false` and
   `npm run build` both pass.

### Signal-based state architecture

`src/app/quotes/quotes.store.ts` — `QuotesStore`, `providedIn: 'root'`:

```ts
private readonly _quotes = signal<Quote[]>([]);
private readonly _loading = signal(false);
private readonly _error = signal<string | null>(null);
private readonly _authorFilter = signal('all');

readonly quotes = this._quotes.asReadonly();
readonly loading = this._loading.asReadonly();
readonly error = this._error.asReadonly();
readonly authorFilter = this._authorFilter.asReadonly();
```

Four source-of-truth signals, all private, all exposed publicly only via
`.asReadonly()`. Nothing outside the store can call `.set()`/`.update()` on
them — every state change goes through `load()` or `selectAuthor()`. This
was checked, not assumed: `quotes.store.spec.ts` and `quotes.spec.ts` both
assert `(store.quotes as any).set` is `undefined` on the readonly signal.

`src/app/quotes/quotes.ts` (the `Quotes` component) was cut down to a
presentational shell: it injects `QuotesStore`, re-exposes its readonly
signals directly (`readonly quotes = this.store.quotes;` etc.), and forwards
the two user actions (`load()`/`selectAuthor()`) back to the store. The only
state it still owns itself is route-level UI state that has nothing to do
with the quotes feature — the `authRequiredNotice` banner flag from Task 1's
guard, and a `lastEffectRun` signal used for the `document.title` effect.

**Where did the Task 1 "selected quote" go?** It didn't — selection is
already owned by the router (`/quotes/:id`, read in `QuoteDetail` via
`ActivatedRoute.paramMap`), not by a `selectedQuote` signal in the store.
Duplicating that as a second piece of state here would violate requirement
2 ("do not maintain duplicate state manually when it can be derived") in
spirit: the URL is already the source of truth for "which quote is open,"
so the store doesn't need its own copy of it.

### Derived state (`computed()`)

```ts
readonly authors = computed(() => {
  const unique = new Set(this._quotes().map((q) => q.author));
  return ['all', ...Array.from(unique).sort()];
});

readonly authorCount = computed(() => this.authors().length - 1);

readonly filteredQuotes = computed(() => {
  const filter = this._authorFilter();
  return filter === 'all' ? this._quotes() : this._quotes().filter((q) => q.author === filter);
});

readonly quoteCount = computed(() => this.filteredQuotes().length);

readonly isEmpty = computed(
  () => !this._loading() && !this._error() && this.filteredQuotes().length === 0,
);
```

Nothing here is stored twice. `authors`/`authorCount` depend only on
`_quotes` (the full set — filtering to one author must not make the
"Authors" stat drop to 1, and a test asserts exactly that).
`filteredQuotes`/`quoteCount`/`isEmpty` depend on both `_quotes` and
`_authorFilter`, and update automatically whenever either changes — no
component ever recomputes a count by hand. `quotes.html`'s empty-state
check and "Authors" stat, which previously did `filteredQuotes().length ===
0` and `authors().length - 1` inline in the template, now just call
`isEmpty()` and `authorCount()`.

### Real API / fields

`QuotesStore.load(page = 1, size = 50)` calls the existing
`QuotesApi.getQuotes(page, size)`, which hits the real, unchanged
`GET /api/quotes?page=1&size=50` through the same `provideHttpClient(withInterceptors(API_INTERCEPTORS))`
chain from Task 1 (activity → auth → error-mapping → retry). Nothing new
was added to that chain. `Quote` fields are exactly `{ id: number, author:
string, text: string, isDeleted: boolean }` — unchanged from the model
Day-15 already defined; nothing invented.

### State vs. computed state

| Kept as a signal (source of truth) | Derived with `computed()` |
|---|---|
| `_quotes` — the raw API response | `authors`, `authorCount` |
| `_loading` | `filteredQuotes`, `quoteCount` |
| `_error` | `isEmpty` |
| `_authorFilter` | — |

Rule applied: a value is a signal only if something *sets* it directly
(an API response, a user picking a filter). Anything that can be computed
purely from other signals is `computed()`, full stop — this is also why the
one bug below (see next section) was a state-*sequencing* bug, not a
duplicated-state bug: duplication was avoided from the start, but a store
can still race with itself.

### Verification (real API + real app, actually run)

The Day-16 dev server (port 4200) and the real Day-5 `QuotesApi` (port
5147) were both already running from Task 1's verification; Task 2 was
verified against that same live pair with a fresh Playwright session, plus
the store/component unit tests below.

- **Initial loading** — `loading()` is `true` immediately after `load()` is
  called (asserted in `quotes.store.spec.ts` before the mock HTTP request
  is flushed), and the skeleton grid renders in the real app during the
  real request.
- **Successful quote loading** — live run: 28 real cards rendered; stat row
  showed `28 / 18 / 28` (quotes / authors / showing), all three computed
  from the same `_quotes` signal.
- **Empty list** — covered as a unit test (`isEmpty` returns `true` when
  `filteredQuotes()` is empty), since the real API's author dropdown only
  ever lists authors that exist, so there's no way to reach a genuinely
  empty *filtered* list through the real UI without also proving the
  underlying signal logic, which the test does directly.
- **API error** — a real `500` was injected at the network layer with
  Playwright's `page.route()` (same technique Day-15's README used for
  `503`s — the real interceptor chain does the handling, not a fake UI
  state). Live result: `.error-card` shown, message "The Quotes API request
  failed. Please try again.", status pill flipped to "Offline", zero
  `.quote-grid` rendered. Clicking "Try again" afterward re-ran `load()`
  against the real (un-mocked) API and successfully recovered — `.error-card`
  gone, all 28 cards back, confirming an error does **not** survive a
  successful reload (also unit-tested directly).
- **Filter changes** — live run: selecting "Ada Lovelace" dropped "Showing"
  from `28` to `7` while "Authors" stayed `18`, and every rendered card's
  author text was confirmed to equal the selected author.
- **Computed quote count updating** — the "Showing" stat and the rendered
  card count both come from `quoteCount()`/`filteredQuotes()` and moved
  together, live, on every filter change.
- **Computed author count updating** — confirmed it does *not* change when
  filtering (`authorCount()` is derived from the unfiltered `_quotes`), both
  live and as a unit test — this is also the exact class of bug the "authors
  count" would have if it had accidentally been derived from
  `filteredQuotes` instead.
- **Selected quote updating** — selection lives in the route (Task 1), not
  in this store; covered by Task 1's `/quotes/:id` verification, not
  duplicated here.
- **Concurrent/repeated refresh** — see "Bug caught and fixed" below; this
  is where the one real bug was found.

`npm test -- --watch=false`: **10 test files, 47/47 passing** (36 inherited
from Task 1 + 2 new files, `quotes.store.spec.ts` and `quotes.spec.ts`, with
11 new tests). `npm run build`: clean, lazy chunks for `create-quote` and
`quote-detail` unchanged from Task 1.

### Bug caught and fixed

**Assumption:** subscribing to `quotesApi.getQuotes()` and setting
`_quotes`/`_loading` straight from the `next`/`error` callbacks would be
correct for `load()`, since that's exactly what the pre-existing Day-15
`Quotes`/`QuotesList` components already did.

**Reality (found by writing the "concurrent/repeated refresh" test
requirement 5 explicitly calls for, not by inspection):** nothing stops two
`load()` calls from being in flight at once — e.g. a user double-clicking
"Try again," or revisiting `/quotes` while a slow request from the previous
visit hasn't resolved yet (the store is `providedIn: 'root'`, so it
outlives any single `Quotes` component instance). Two in-flight requests
resolve in **whatever order the network delivers them**, not necessarily
the order they were sent. A unit test made this concrete and reproducible:

```ts
store.load();                     // request #1 sent
const firstReq = httpMock.expectOne(...);
store.load();                     // request #2 sent, #1 still in flight
const secondReq = httpMock.expectOne(...);

secondReq.flush([QUOTE_B]);       // #2 (newer) resolves first
firstReq.flush([QUOTE_A]);        // #1 (older, now-stale) resolves after it

expect(store.quotes()).toEqual([QUOTE_B]); // FAILED before the fix: got [QUOTE_A]
```

Before the fix, the store ended up showing `QUOTE_A` — the result of the
request that was fired *first*, not the one fired *last* — because the
`next`/`error` callbacks unconditionally overwrite `_quotes`/`_error`
whenever they fire, with no notion of "is this response still relevant."
That's a real, reachable bug: a user who clicks "Try again" twice in a row
while offline-then-recovering could see the list silently revert to a
result that's already out of date.

**Fix:** a `latestRequestId` counter, incremented on every `load()` call and
captured per-request; both the `next` and `error` callbacks check the
captured id against the counter's *current* value before touching any
signal, and bail out silently if a newer `load()` has since started:

```ts
private latestRequestId = 0;

load(page = 1, size = 50): void {
  const requestId = ++this.latestRequestId;
  this._loading.set(true);
  this._error.set(null);

  this.quotesApi.getQuotes(page, size).subscribe({
    next: (data) => {
      if (requestId !== this.latestRequestId) return; // stale response, ignore
      this._quotes.set(data);
      this._loading.set(false);
    },
    error: (err) => {
      if (requestId !== this.latestRequestId) return;
      this._error.set(err.message);
      this._loading.set(false);
    },
  });
}
```

Re-ran the same test after the fix: `store.quotes()` now correctly ends up
as `[QUOTE_B]` (the latest request's result) regardless of which response
lands on the wire first. All 47 tests pass.

### Why Signals + service is enough for this feature right now

This feature is one store, four source signals, five `computed()`s, and two
mutating methods (`load`, `selectAuthor`), used by exactly one component.
There's no cross-feature state sharing (the guard, the API-activity panel,
and the detail route each own their own local state and don't read
anything from `QuotesStore`), no multi-step workflow or saga-like sequencing
(loading a page of quotes is one request, not a chain of dependent effects),
and nothing here needs replay/undo or a global action log to debug — the
one real bug found above was diagnosed and fixed with an ordinary unit
test, not a time-travel debugger. Angular signals already give this feature
everything NgRx would be brought in for at this scale: a single
observable-like source of truth (`asReadonly()` signals), free automatic
memoized derivation (`computed()`), and OnPush-equivalent fine-grained
change detection for zoneless mode — without actions, reducers, selectors,
or effects boilerplate for four fields and two methods. Introducing NgRx
here would mean writing an action, a reducer case, and a selector for
`selectAuthor(author)` — a one-line `signal.set()` today — for no behavior
this app actually needs yet.

### When I would reach for NgRx/a store instead

Not never — just not here, not yet. Concretely, I'd reach for it when:

- **Many unrelated features genuinely need to share the same state** —
  e.g. a shopping-cart total that a header badge, a checkout page, and a
  promotions banner all need to read and write independently, where
  passing a service reference around stops being enough because the
  *shape* of shared state itself needs a stable, inspectable contract.
- **State transitions get complex enough that "which state can follow
  which" needs to be enforced**, not just implied by which methods happen
  to get called — e.g. an order-checkout flow with real guarded
  transitions (`draft → submitted → paid → fulfilled`, each with different
  allowed side effects), where a reducer's exhaustive `switch` catches an
  invalid transition at compile time in a way a pile of `signal.set()`
  calls doesn't.
- **Real effects/workflows** — chains of dependent async work, retries with
  backoff coordinated across multiple sources, cancellation policies that
  differ per action type — where NgRx's Effects (or a comparable operator
  pipeline) meaningfully organizes what would otherwise be nested
  `.subscribe()` calls. This app's `load()` is one request; there's no
  chain to coordinate.
- **Centralized debugging/time-travel is an actual requirement** — e.g. a
  complex editor or multi-step form where support needs to replay a user's
  exact action sequence to reproduce a bug. Redux DevTools' action log is
  built for that; a handful of `signal()`s isn't, and nothing in this app
  currently needs it.
- **Application-wide state genuinely outgrows "a few services"** — dozens
  of features, deep cross-cutting concerns (auth, feature flags,
  notifications, multi-entity caches with normalization), where an ad hoc
  collection of root-scoped signal stores starts duplicating the very
  selector/normalization machinery a store library gives you for free.

None of that describes this feature. If a second feature later needs to
read `QuotesStore`'s state, that's still not automatically a reason for
NgRx — it's a reason to check whether a shared signal-based store (or this
same one, injected elsewhere) is still enough first.

### What would break if the Week-1 API contract changed

- **`GET /api/quotes` response shape changed** (bare array → an enveloped
  `{ data: [...], total: N }`): `QuotesApi.getQuotes()`'s
  `Observable<Quote[]>` typing wouldn't match reality; `_quotes.set(data)`
  would store the envelope object as if it were an array, and every
  `computed()` in the store that calls `.map`/`.filter`/`.length` on it
  (`authors`, `filteredQuotes`, `quoteCount`) would throw at runtime the
  first time a component reads them — a compile-time type mismatch would
  not catch this, since nothing validates the network response shape at
  runtime (same risk already documented for Task 1's detail endpoint).
- **A `Quote` field were renamed** (e.g. `author` → `authorName`): `authors`
  (built from `q.author`) would silently produce a list of `undefined`
  values instead of erroring, `authorCount` would report `1` (one
  "author": `undefined`) instead of the real count, and every quote card
  would render an empty author name — all without the store or its tests
  ever throwing, since the store trusts `QuotesApi`'s TypeScript typing
  rather than validating the live response shape.
- **`page`/`size` query params were renamed or dropped by the API**:
  `QuotesStore.load()` would still send `page`/`size` params the server
  now ignores; the app wouldn't error, it would just silently always get
  whatever default page/size the server falls back to — the store has no
  way to detect "the params I sent didn't do anything," same limitation
  Task 1's README already documented for the list endpoint.
- **The API started paginating differently** (e.g. requiring a cursor
  instead of `page`): `load(page, size)`'s signature and the store's single
  "one full page, no pagination controls" model would need to change
  together; nothing downstream (`computed()`s, the component) depends on
  `page`/`size` being numbers specifically, so that part would be a
  contained change to `load()` and `QuotesApi.getQuotes()` only.

### Screenshots

- `Day-16/screenshots/day16-task2-loaded.png` — `/quotes`, real data
  loaded: `28` quotes, `18` authors, `28` showing.
- `Day-16/screenshots/day16-task2-filtered.png` — same page after selecting
  "Ada Lovelace" in the author filter: `28 / 18 / 7`, only that author's
  quotes rendered.
- `Day-16/screenshots/day16-task2-error.png` — a real `500` injected at the
  network layer (Playwright `page.route()`, real interceptor chain handling
  it): "Something went wrong" / "The Quotes API request failed. Please try
  again.", status pill "Offline", zero quote cards rendered.

No JWT/token value, `Authorization` header, or credential appears in any
screenshot, the DOM, or this document.
