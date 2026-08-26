# Day 15 — Task 1: HttpClient + Interceptors

Builds on the Day-14 Angular app (`quotes-angular`, copied into `Day-15/quotes-angular`
and extended — Day-14 itself was not modified). Adds a retry interceptor and a
typed error-mapping layer on top of the existing `HttpClient` + auth-interceptor
setup, verified against the real, running Day-5 `QuotesApi`.

## Brief

1. Write a characterization test against the real API *first*, before touching the UI.
2. Use Angular `HttpClient` for all API calls (no hand-written `fetch`).
3. An `HttpInterceptorFn` attaches `Authorization: Bearer <token>` from the local
   dev-auth setup, without ever hard-coding or committing a real token.
4. A retry interceptor retries transient failures for GET only, with a small
   backoff. POST/PUT/PATCH/DELETE are never auto-retried.
5. Map ASP.NET `ProblemDetails` / `ValidationProblemDetails` into a small typed
   `AppError` so the UI gets a friendly message, not raw JSON.
6. Actually run both the real API and the real app and verify all of the above.
7. Catch and document one real wrong assumption from testing against the live API.
8. `ng test` and `ng build` pass.

## Real API contract (as verified against the running Day-5 QuotesApi)

```
GET /api/quotes?page=1&size=50
  -> 200, JSON array of:
     { id: number, author: string, text: string, isDeleted: boolean }

GET /api/quotes/{id}          -> 404, EMPTY body if not found
POST /api/quotes (no token)   -> 401, EMPTY body
POST /api/quotes (invalid body, valid token, quotes.write scope)
  -> 400, ValidationProblemDetails:
     {
       "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
       "title": "One or more validation errors occurred.",
       "status": 400,
       "errors": { "quote": ["Author must be between 1 and 200 characters."] },
       "traceId": "..."
     }
POST /api/quotes (valid)      -> 201, created Quote
```

## Characterization test (ran first)

`src/app/characterization/quotes-api.characterization.spec.ts` hits the real,
running API directly at `http://localhost:5147` — no mocks, no interceptors —
and asserts:

- `GET /api/quotes?page=1&size=50` succeeds and returns a non-empty array
  whose items carry `id`, `author`, `text`, `isDeleted` with the right types.
- `GET /api/quotes/999999` returns a bare 404 with **no body** (this shaped
  the error mapper — see "Bug caught" below).
- `POST /api/quotes` with no token returns a bare 401 with no body.
- `POST /api/quotes` with a valid local dev token and an invalid body returns
  a real `ValidationProblemDetails` 400 (skipped automatically if no local
  dev token is present, since that path needs auth).

Result: **3/3 unconditional tests pass**, plus the auth-gated one, against
the live API (13/13 tests total across all three spec files — see below).

## Interceptors

- `src/app/auth/auth.interceptor.ts` (pre-existing, unchanged): attaches
  `Authorization: Bearer <token>` to `/api/*` requests when a token exists in
  `localStorage`, loaded at app-init from the gitignored
  `dev-config/dev-token.local.json` (never committed, never hard-coded).
- `src/app/http/retry.interceptor.ts` (new): for `GET` requests only, retries
  up to 2 times on transient failures (`status 0`, `502`, `503`, `504`) with
  exponential backoff (200ms, 400ms). Any other method, or a non-transient
  status (4xx), passes straight through with no retry.
- `src/app/http/error-mapping.interceptor.ts` (new): catches whatever error
  survives the retry interceptor and maps it into a typed `AppError` —
  `{ status, message, validationErrors? }` — from the real `ProblemDetails` /
  `ValidationProblemDetails` shape, or a friendly fallback when the body is
  empty (401/403/404) or the request never reached the server (`status 0`).

Registered in `app.config.ts` as
`withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])`.
Order matters: `retryInterceptor` is closest to the backend so retries reuse
the already-authenticated request; `errorMappingInterceptor` sits outside it
so it only maps the *final* error, after retries are exhausted.

Existing components (`create-quote.ts`, `create-quote-signal.ts`,
`quotes.ts`, `quotes-list.ts`) were updated to consume `AppError` from
subscribe/catch handlers instead of raw `HttpErrorResponse`, so `err.message`
is always a ready-to-display string.

## Verification (real API + real app, actually run)

Environment note: this sandbox has no `Jwt:Key` configured anywhere (not in
`appsettings*.json`, not in `dotnet user-secrets`, not in the environment —
confirmed by running the repo's own `QuotesApi.Tests`, which also fail here
with `JWT key is not configured`), and the token checked into
`dev-config/dev-token.local.json` from Day-14 had expired. For local
verification only, a random local-only signing key was generated
(`crypto.randomBytes`, kept in `/tmp`, never printed or committed) and passed
to the API via the `Jwt__Key` environment variable; a fresh token was minted
with it and written to the same gitignored `dev-config/dev-token.local.json`
Day-14 already uses. No real/production token or key appears anywhere in the
diff.

Commands run:

```bash
# Terminal 1 — real API
cd Day-5/QuotesApi
Jwt__Key=<local-only-generated-secret> dotnet run

# Terminal 2 — real app
cd Day-15/quotes-angular
npm install
npx ng test --watch=false     # characterization + interceptor unit tests
npx ng build                  # production build
npx ng serve --port 4200      # dev server, proxies /api to :5147
```

Results:

- `ng test`: **3 test files, 13/13 tests passed** (characterization against
  the live API, `error-mapping.interceptor.spec.ts`, `retry.interceptor.spec.ts`).
- `ng build`: production bundle builds clean, 279.83 kB raw / 72.49 kB transfer.
- `ng serve` + a real headless-browser session (Playwright) against the live
  app, driven end to end:
  - `GET /api/quotes?page=1&size=50` → **200**, `Authorization` header present,
    quote list rendered ("Browse" tab), no console errors.
  - Submitted a real quote via the "Create" tab → **POST /api/quotes → 201**,
    `Authorization` header present (proves the auth interceptor attaches the
    local dev token on a real write through the whole app, not just in
    isolation) — UI showed "Quote created — Quote #26 by Playwright
    Verification was saved successfully."
  - The reactive form's client-side validators mirror the server's rules
    1:1, so a real user can never submit a request that trips the server's
    400 — to still prove the error-mapping path end to end, the running
    app's injected `QuotesApi` service was called directly (bypassing the
    form) with an invalid body. Result, from the **real live 400**:
    `{"status":400,"message":"Author must be between 1 and 200 characters.","validationErrors":{"quote":["Author must be between 1 and 200 characters."]}}`
    — i.e. the friendly `AppError`, not the raw ProblemDetails JSON.
- GET retry/backoff and "POST is never auto-retried" are verified by
  `retry.interceptor.spec.ts` using Angular's `HttpTestingController` against
  the real interceptor code (three real transient-failure/backoff scenarios:
  retry-then-succeed, retry-until-exhausted, no-retry-on-POST, no-retry-on-4xx).
  Simulating a transient 503/504 from the real live API wasn't practical
  without modifying Day-5, so this one piece is unit-tested rather than
  captured live; everything else above ran against the real, running API.

## Bug / wrong assumption caught

**Assumption:** Since `Program.cs` calls `builder.Services.AddProblemDetails()`,
I assumed every 4xx response from the API — including a bare `404` from
`GET /api/quotes/{id}` and the `401` from a missing token — would come back
as a structured `ProblemDetails` JSON body.

**Reality (found by actually curling the running API):**

```
$ curl -i http://localhost:5147/api/quotes/999999
HTTP/1.1 404 Not Found
(empty body)

$ curl -i -X POST http://localhost:5147/api/quotes -d '{"author":"X","text":"Y"}'
HTTP/1.1 401 Unauthorized
(empty body)
```

`AddProblemDetails()` only fills in a body for responses your code
*explicitly* builds through it (`Results.ValidationProblem(...)`, the custom
`AddExceptionHandler` in `Program.cs`). Bare `Results.NotFound()` and the
framework's own 401 challenge never get a JSON body in this API.

**Fix:** the original plan was to capture the "real 4xx" for the
characterization test from `GET /api/quotes/{id}` (simplest, no-auth
endpoint). That had to change — it was switched to the authenticated
`POST` validation-error path, since that's the only endpoint in this API
that actually returns a `ValidationProblemDetails` body. The 404/401 branches
in `error-mapping.interceptor.ts` were written to tolerate a `null`/empty
`error.error` from the start (`body?.detail ?? body?.title ?? '<fallback>'`,
and a dedicated 401/403 branch that never reads the body), so no crash would
have occurred even without this correction — but the *test plan* and the
docs above it were wrong until this was actually verified against the live
API, which is exactly what the characterization-test-first step is for.

## What would break if the real GET contract changed

- **Renamed/removed fields** (e.g. `author` → `authorName`): the
  characterization test fails immediately (that's its job). Downstream,
  `quotes.ts`'s `authors`/`filteredQuotes` computed signals and
  `quotes-list.ts`'s rendering would silently show `undefined` instead of
  throwing, since nothing currently validates the response shape at runtime
  beyond the characterization test.
- **Response wrapped in an envelope** (e.g. `{ data: [...], total: N }`
  instead of a bare array): `Array.isArray` in the characterization test
  fails first; in the app, `QuotesApi.getQuotes()`'s `Observable<Quote[]>`
  typing would no longer match reality, and every `.map`/`.filter`/`.length`
  call in `quotes.ts` / `quotes-list.ts` would throw at runtime instead of
  failing at compile time, since TypeScript types aren't checked against the
  real network response.
- **`page`/`size` query params renamed or removed**: `QuotesApi.getQuotes()`
  would silently send params the API ignores, likely returning a default
  page instead of erroring — the characterization test's array-shape
  assertions would still pass, but it wouldn't catch a param name change on
  its own; only a response-content assertion (e.g. requesting `size=1` and
  asserting exactly one row) would.
- **Retry and error-mapping interceptors are unaffected** by a GET
  success-shape change — they only inspect `req.method` and
  `HttpErrorResponse.status`/`.error`, not the success body.

---

## Task 2: "API Activity" / Request Inspector panel

Adds a small floating panel (bottom-right, toggleable) that makes the
interceptor chain above visibly demonstrable in the running app — driven by
real request/response/error events from the real interceptors, not a
UI-only simulation.

### Architecture

- `src/app/http/api-activity.model.ts` — `ApiActivityEntry`: `{ id, method,
  path, state, status?, retryAttempt?, authAttached?, message?, updatedAt }`.
  `state` is one of `pending | retrying | success | error`. No token field
  exists anywhere in this model.
- `src/app/http/api-activity.service.ts` — `ApiActivityService`, a
  `providedIn: 'root'` signal store holding the last 20 entries
  (`recent()`, newest first) and a derived `connectionStatus()`
  (`unknown | online | offline`, based on the most recent settled request —
  `offline` only for a network-level failure, i.e. `status === 0`; any real
  HTTP response, even a 4xx/5xx, counts as `online`).
- `src/app/http/api-activity.token.ts` — `API_ACTIVITY_ID`, an
  `HttpContextToken<number | null>`. `HttpContext` is client-side-only and
  shared by reference across `req.clone()` calls, so it's the correct
  Angular mechanism to correlate one logical request across interceptors
  without adding a real header sent over the wire.
- `src/app/http/activity.interceptor.ts` — **new**, registered outermost.
  Opens one activity entry per request (`activity.start(method, path)`),
  stores the id on `req.context`, and closes the entry with whatever the
  rest of the real chain produced: `HttpEventType.Response` → `succeed(id,
  status)`; a thrown error → `fail(id, appError.status, appError.message)`.
  Because it sits *outside* `errorMappingInterceptor`, the error it observes
  is already the mapped `AppError` — never raw `HttpErrorResponse`/JSON.
- `src/app/http/retry.interceptor.ts` — **modified**: on each transient
  retry, calls `activity.retrying(id, attemptNumber)` before the backoff
  `timer()`, reading `id` from `req.context`.
- `src/app/auth/auth.interceptor.ts` — **modified**: reports
  `activity.setAuthAttached(id, true/false)` based on the same condition it
  already uses to decide whether to attach the header — it never reports
  (or stores, or renders) the token value itself, only the boolean.
- `app.config.ts` interceptor order is now `[activityInterceptor,
  authInterceptor, errorMappingInterceptor, retryInterceptor]` —
  `activityInterceptor` outermost so it captures the *final* outcome after
  retries/error-mapping; `retryInterceptor` innermost, unchanged from Task 1.
- `src/app/api-activity-panel/` — the presentational component. Reads
  `ApiActivityService.recent()` / `.connectionStatus()` directly (signals,
  zoneless) and renders each entry: method badge, path, state, `HTTP
  {status}`, a `Retry attempt N` chip only when `retryAttempt` is set, and
  an `Authorization attached` / `No credentials` chip. An error entry shows
  `entry.message` (the mapped `AppError.message`) — never `entry` raw JSON,
  never a header value. Styled with the app's existing design tokens
  (`--color-*`, `--radius-*`, `--shadow-*` from `styles.css`) as a floating
  toggle + card, consistent with the rest of the UI. Mounted once in
  `app.html`, outside the tab `@if`/`@else` blocks, so it persists across
  Browse/Explorer/Create/Create (Signal Forms) and reflects traffic from any
  of them.

### Tests (new)

- `api-activity.service.spec.ts` — state-transition and
  `connectionStatus()` unit tests, including an explicit assertion that a
  serialized entry never contains the word `"Bearer"` or a `token` field.
- `api-activity.integration.spec.ts` — runs the **real** chain
  (`activityInterceptor` + `authInterceptor` + `errorMappingInterceptor` +
  `retryInterceptor`, exactly as wired in `app.config.ts`) against
  `HttpTestingController`, and asserts the service ends up in the right
  state for: a successful authenticated GET; a GET with no token
  (`authAttached: false`); a real `ValidationProblemDetails` 400 mapped to
  its friendly message; a transient GET failure that retries once then
  succeeds; a POST that fails transiently and is never retried.
- All existing Task 1 tests (`retry.interceptor.spec.ts`,
  `error-mapping.interceptor.spec.ts`, the characterization spec) still
  pass unmodified except one line in the characterization spec (see
  "Verification" below).

`ng test`: **5 test files, 24/24 passing.** `ng build`: clean.

### Verification (real API + real app, actually run)

Environment note: between the previous session and this one, the user
started their own `dotnet run` (Day-5 `QuotesApi`, port 5147) and `ng serve`
(Day-14, port 4200) in separate terminals. Both were left running and
untouched — a **separate** `ng serve --port 4201` was started for Day-15 so
verification didn't collide with that session, and it was stopped again
afterward. The user's API instance uses its own `Jwt:Key`, which the
Day-15 dev token (signed with a different local-only key from the previous
session) doesn't validate against — so authenticated `POST` calls in this
verification run got a real `401` rather than a `201`/`400`. That's still a
genuine, real 4xx from the live API (useful for check 2 below); it just
meant the characterization spec's optional `ValidationProblemDetails` test
needed a one-line update to skip gracefully on a `401` instead of asserting
`400`, documented inline in that test.

Driven with a real headless-browser session (Playwright) against
`http://localhost:4201`, proxying to the real, running API, using
`page.route()` to deterministically inject transient `503`s at the network
layer (so the app's own interceptors — not a test double — do the
retrying):

1. **Normal `GET /api/quotes?page=1&size=50` → success.** Panel entry:
   `GET /api/quotes?page=1&size=50 · SUCCESS · HTTP 200 · Authorization attached`.
2. **A real 4xx shows the mapped friendly error.** Submitting the create
   form hit the live API's real auth check and got a genuine `401`; the
   panel and the form both showed *"You are not authorized to perform this
   action."* — never the raw response body.
3. **Transient GET failure → retrying → success/error**, both outcomes
   demonstrated: (a) first two `GET`s intercepted as `503`, third let
   through — panel showed `Retry attempt 2` then `SUCCESS`; (b) all `GET`s
   forced to `503` — panel showed `ERROR · HTTP 503 · Retry attempt 2 · "The
   Quotes API request failed. Please try again."` after retries were
   exhausted.
4. **POST is not shown as automatically retried.** A `POST` forced to
   `503` was hit by the route handler exactly **once** (asserted via the
   route handler's own hit counter) — the panel shows `ERROR · HTTP 503`
   with **no** `Retry attempt` chip, unlike every `GET` failure above.
5. **No JWT/token value in the DOM.** After exercising every scenario
   above (so a real, valid-looking bearer token had been attached to
   multiple real requests), the full rendered page HTML was checked for the
   literal dev-token string, a 40-character prefix of it, and the word
   `"Bearer"` — all three: **absent**. The panel only ever renders the
   `authAttached` boolean.
6. **`ng test` / `ng build`** — 24/24 passing, clean production build (see
   above).
