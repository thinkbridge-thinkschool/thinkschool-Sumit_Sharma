# Day 16 — Task 1: Routing, Lazy Loading, Guards

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

- `Day-16/screenshots/day16-task1-list.png` — `/quotes`, real data, 27
  quotes, API-connected status.
- `Day-16/screenshots/day16-task1-detail.png` — `/quotes/1`, real quote,
  showing author, text, id, and the back link.
- `Day-16/screenshots/day16-task1-guard.png` — a genuinely unauthenticated
  session redirected by the guard from `/create` to
  `/quotes?authRequired=true`, showing the "Sign in to create a quote."
  banner.

No JWT/token value appears in any screenshot, the DOM, or this document.
