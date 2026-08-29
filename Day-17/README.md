# Day 17 — Deploy to Azure Static Web Apps

## Status up front

The Azure Static Web Apps *resource itself* cannot be created in this
subscription — confirmed twice, independently, against live Azure state (see
"The blocker"). That is a subscription-level policy restriction on a
university-managed "Azure for Students" tenant, not something fixable from
inside a coding session.

Given that hard constraint, everything else in the brief has actually been
delivered as a **real, live deployment** — just on Azure Container Apps
(already proven usable in this subscription/region) instead of Azure Static
Web Apps, using the exact same architecture (static Angular build + a small
server-side compute layer holding a system-assigned Managed Identity) that
Static Web Apps' own "Managed Functions" would have provided:

- **Live URL:** https://quotes-web.bravebay-90a32791.centralindia.azurecontainerapps.io
- **Lighthouse:** Performance 96 (mobile) / 100 (desktop), Accessibility 100,
  Best Practices 100, SEO 100 — run against the live URL above, not a local
  emulator.
- **Managed Identity, no secret:** a real Entra access token, issued by Azure
  to the Container App's own system-assigned identity, reaches the real
  Week-1 API on every `/api/*` call. Captured and verified below with the
  actual token claims. No client secret, connection string, or certificate
  exists anywhere in the repo or in the app's configuration — confirmed by
  listing every configured setting.
- **Custom domain:** none available this round (no DNS zone exists in this
  subscription) — documented as not-applicable, not faked.
- The Azure Static Web Apps build (`staticwebapp.config.json`, the
  MI-authenticated Azure Functions proxy in `api/`, the CI workflow) is
  100% built and untouched, ready to deploy in the exact commands below the
  moment the region policy is lifted or a different subscription is used.

## The brief I gave the agent (Claude Code)

> Deploy `Day-17/quotes-angular` (Angular 21, copied forward from the
> completed Day-16 app: routing, guards, signals store, soft login/logout,
> real delete) to Azure Static Web Apps, with a custom domain if one is
> available. The app must call the real Week-1 API — the `quotes-api`
> Container App already live at
> `https://quotes-api.bravebay-90a32791.centralindia.azurecontainerapps.io`
> (`GET /api/quotes`, `GET /api/quotes/{id}`, `POST /api/quotes`,
> `DELETE /api/quotes/{id}`) — via a Managed Identity, not a stored client
> secret. Verify: the live URL loads, a Lighthouse run scores ≥95, and the
> call to the API genuinely carries a Managed-Identity-issued token with no
> secret anywhere in the repo or app settings. If Static Web Apps itself
> turns out to be genuinely unreachable in this subscription, don't fake a
> deployment — prove the blocker, then get the same architecture live on
> whatever Azure compute *is* reachable, and keep the SWA build ready to go.

## Why "Managed Identity" can't mean what it sounds like at first

A browser can never hold a Managed Identity — MI is an Azure-compute-only
credential (issued to a VM, an App Service, a Function App, a Container App,
etc. by the platform itself). So "call the Week-1 API via Managed Identity"
has to mean a small piece of **server-side** compute sits between the
browser and the real API. Azure Static Web Apps' built-in "Managed
Functions" (an `api/` folder, bundled and hosted by the SWA itself) is
exactly that piece. Azure Container Apps' own system-assigned identity is
the same idea on different compute — which is what actually made a live,
honest deployment possible in this subscription today.

## The blocker (Azure Static Web Apps itself)

Re-verified live against the subscription today, independently of any
earlier session's notes:

```
$ az policy assignment list --query "[?name=='sys.regionrestriction'].parameters"
[{
  "listOfAllowedLocations": {
    "value": ["koreacentral", "uaenorth", "indonesiacentral", "malaysiawest", "centralindia"]
  }
}]

$ az provider show --namespace Microsoft.Web \
    --query "resourceTypes[?resourceType=='staticSites'].locations"
[["Central US", "East US 2", "West US 2", "West Europe", "East Asia"]]
```

Zero overlap between the subscription's five allowed regions and Static Web
Apps' five supported regions worldwide — this is a platform-wide limitation
(SWA only ever ships in those five regions, full stop) crossed with a
subscription-wide policy (this tenant only allows five different ones).

Confirmed live, not just from provider metadata — attempted real creates
against both an SWA-supported region and a policy-allowed region:

```
$ az staticwebapp create -n thinkschool-quotes-swa -g thinkschool-rg --sku Free -l eastasia
ERROR: (RequestDisallowedByAzure) Resource 'thinkschool-quotes-swa' was disallowed by Azure:
This policy maintains a set of best available regions where your subscription can deploy
resources... Should you need additional or different regions, contact support.

$ az staticwebapp create -n thinkschool-quotes-swa-test -g thinkschool-rg --sku Free -l centralindia
ERROR: (LocationNotAvailableForResourceType) The provided location 'centralindia' is not
available for resource type 'Microsoft.Web/staticSites'. List of available regions for the
resource type is 'centralus,eastus2,westus2,westeurope,eastasia'.
```

Both failed creates left no orphaned resources (`az staticwebapp list -g thinkschool-rg`
returns empty). This is a real, reproducible, two-sided dead end — the fix is
either a policy exception from the subscription/tenant admin or a different
subscription, neither of which a coding session can grant itself. `az
account list` also confirms there is only the one subscription available.

No custom domain was available either way (`az network dns zone list`
returns nothing in this subscription) — documented as N/A, not faked with a
placeholder.

## Architecture (what's actually live)

```
Browser (Angular app)
  │  same-origin fetch, e.g. GET /api/quotes  — no token, no secret, ever
  ▼
Azure Container App: quotes-web (thinkschool-rg / centralindia)
  ├─ Static assets: dist/quotes-angular/browser, served by a small Express
  │  server (server/server.js) — the same role staticwebapp.config.json
  │  and SWA's static hosting would play: SPA fallback, security headers,
  │  Brotli/gzip compression, long-lived immutable caching on hashed assets.
  └─ /api/* proxy, on every request:
       1. DefaultAzureCredential resolves to quotes-web's own
          system-assigned Managed Identity (in Azure) - no secret,
          no certificate, no connection string configured anywhere.
       2. Acquires a real Entra access token for
          api://697131d5-940f-4d27-a027-8f2284907c64/.default
       3. Forwards the request to the real Container App with
          Authorization: Bearer <that token> (ignores any auth the
          browser itself sent — the MI token is the only credential
          that ever reaches the real API)
       4. Streams the response back to the browser
  │
  ▼  Authorization: Bearer <Managed-Identity-issued Entra token>
Container App: quotes-api (real Week-1 API, already live)
  EntraJwtScheme validates issuer/audience (from Day 3's Entra ID work) →
  CanEditQuotes policy accepts either:
    - scope=quotes.write   (the internal dev-token scheme — unchanged,
      still used directly by every other day's Angular app)
    - roles contains Quotes.Write   (the Entra app-role a
      Managed-Identity-issued app-only token actually carries)
```

Nothing in this path — not the repo, not the Container App's application
settings, not the server's code — ever holds a client secret, connection
string, or certificate for calling the real API:

```
$ az containerapp show -g thinkschool-rg -n quotes-web --query "properties.template.containers[0].env"
[
  { "name": "QUOTES_API_BASE_URL", "value": "https://quotes-api.bravebay-90a32791.centralindia.azurecontainerapps.io" },
  { "name": "QUOTES_API_SCOPE", "value": "api://697131d5-940f-4d27-a027-8f2284907c64/.default" }
]
```

Both are plain, non-secret application settings — a public FQDN and an
audience URI, not credentials. No `secretRef` for anything auth-related on
this Container App. The only credential in the whole path is the Managed
Identity itself, issued and rotated entirely by Azure — nothing a repo or a
config file could ever leak.

## What was built and deployed this session

### 1. The Angular app (`Day-17/quotes-angular`)

Copied forward from the completed Day-16 app — routing/guards/lazy-loading,
signals-based `QuotesStore`, soft sign-in/out, real delete — with the same
deployment-specific tuning already in place from the earlier pass:
`src/index.html` (real meta description + title), `public/robots.txt`, and
a WCAG-AA-fixed `--color-text-tertiary` in `src/styles.css`.

### 2. `staticwebapp.config.json` (built, ready, not yet deployable)

SPA fallback routing, security headers, and a CSP allowing the
`'unsafe-inline'` `<link onload>` that Angular's own critical-CSS optimizer
("Beasties") emits — untouched this session, still exactly what the real
Static Web Apps deployment will use the moment it can be created.

### 3. The Managed Identity proxy for Static Web Apps (`Day-17/quotes-angular/api/`)

Azure Functions v4 (Node.js) MI proxy for the real SWA path — built and
ready, not yet deployable (see the blocker above).

### 4. The live substitute: `server/server.js` + `Dockerfile` (new this session)

A small Express server that plays the exact same role SWA's static hosting
+ Managed Functions would: serves the built Angular app, proxies `/api/*`
to the real Week-1 API using `@azure/identity`'s `DefaultAzureCredential`
(resolves to the Container App's own system-assigned identity), and sets
the same security headers as `staticwebapp.config.json`. Fixed this session:

- Removed a temporary `/__debug-token-claims` diagnostic route that had been
  left in a prior build (it decoded and returned real MI token claims to
  any caller — a real information-disclosure issue on a public endpoint).
  Used exactly once, deliberately, to capture the token-claims evidence in
  the verification log below, then deleted from the code before the final
  image was built and deployed. Confirmed gone: the route now correctly
  falls through to the SPA catch-all (returns `index.html`, not JSON).
- Found the same pattern one layer deeper while reviewing the diff before
  committing: `Day-5/QuotesApi/Program.cs` still had its own leftover
  `GET /api/debug/whoami` (added during the same earlier debugging pass,
  also marked "removed before final deploy," also never removed) —
  unauthenticated, dumping the full claim set of whatever principal called
  it. Removed, rebuilt (`quotes-api:0.6.0`), redeployed, and re-verified:
  `/health` returns `Healthy`, the debug route now 404s, and the
  Managed-Identity write path still works end to end against the new
  revision (see verification log).
- Added `compression` middleware (Brotli/gzip) — the container had none,
  unlike what Static Web Apps' own CDN provides for free. This alone moved
  the live mobile Lighthouse Performance score from 69 to 96 (see below).
- Added long-lived `Cache-Control: public, max-age=31536000, immutable` on
  Angular's content-hashed static assets (safe because a changed file gets
  a new filename), matching what a CDN-backed static host would set.

### 5. Backend change: `Day-5/QuotesApi/Program.cs` (`CanEditQuotes` policy)

Unchanged from the earlier pass — additive-only change so the policy
accepts **either** the internal dev-token scheme's `scope: quotes.write` or
an Entra app-only token's `roles: Quotes.Write`:

```csharp
options.AddPolicy("CanEditQuotes", policy =>
    policy.RequireAssertion(context =>
        context.User.HasClaim("scope", "quotes.write") ||
        context.User.HasClaim("roles", "Quotes.Write")));
```

Every other day's Angular app still authenticates exactly as before via the
internal dev-token scheme.

Also removed this session: a leftover `GET /api/debug/whoami` diagnostic
endpoint (unauthenticated, dumped the caller's full claim set) — see "What
was built," item 4, for why it was there and how it was found. The API was
rebuilt as `quotes-api:0.6.0` and redeployed to pick up both this and the
unrelated audience-validation fix already in place from the earlier pass.

### 6. Azure resources actually created/configured this session

```
quotes-web                    Microsoft.App/containerApps   (new, this session)
  - system-assigned Managed Identity: aba57120-654e-476f-80e6-683a8780843d
  - app-role assignment: Quotes.Write on the ThinkSchool Quotes API app
    registration (697131d5-940f-4d27-a027-8f2284907c64)
```

```bash
# Grant the new identity the existing Quotes.Write app role
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/<quotes-web-principal-id>/appRoleAssignments" \
  --body '{"principalId":"<quotes-web-principal-id>","resourceId":"<api-service-principal-id>","appRoleId":"4ec0b005-8b23-45bf-aecb-38caaa23ff88"}'
```

The `Quotes.Write` app role itself already existed on the API's app
registration from the earlier Day-17 pass (confirmed via `az ad sp show`,
not recreated).

### 7. CI: `.github/workflows/day17-static-web-app.yml`

Unchanged — a standard `Azure/static-web-apps-deploy@v1` workflow, scoped to
`Day-17/quotes-angular/**`, ready to run the moment
`AZURE_STATIC_WEB_APPS_API_TOKEN` exists as a repo secret (which requires
the SWA resource to exist first). No GitHub CLI/PAT access in this sandbox
to wire that secret automatically — documented as a manual step for the
human, same as before.

## What to run once Azure Static Web Apps is unblocked

```bash
# 1. Create the SWA (replace <region> with whichever gets allow-listed)
az staticwebapp create -n thinkschool-quotes-swa -g thinkschool-rg \
  --sku Free -l <region>

# 2. Enable the Function's system-assigned Managed Identity
az staticwebapp identity assign -n thinkschool-quotes-swa -g thinkschool-rg

# 3. Grant it the existing Quotes.Write app role on the API's app registration
PRINCIPAL_ID=$(az staticwebapp identity show -n thinkschool-quotes-swa \
  -g thinkschool-rg --query principalId -o tsv)
RESOURCE_SP_ID=$(az ad sp show --id 697131d5-940f-4d27-a027-8f2284907c64 \
  --query id -o tsv)
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$PRINCIPAL_ID/appRoleAssignments" \
  --body "{\"principalId\":\"$PRINCIPAL_ID\",\"resourceId\":\"$RESOURCE_SP_ID\",\"appRoleId\":\"4ec0b005-8b23-45bf-aecb-38caaa23ff88\"}"

# 4. Configure the proxy's non-secret app settings
az staticwebapp appsettings set -n thinkschool-quotes-swa -g thinkschool-rg \
  --setting-names \
  QUOTES_API_BASE_URL=https://quotes-api.bravebay-90a32791.centralindia.azurecontainerapps.io \
  QUOTES_API_SCOPE=api://697131d5-940f-4d27-a027-8f2284907c64/.default

# 5. First deploy, directly
cd Day-17/quotes-angular && npm run build -- --configuration production
DEPLOY_TOKEN=$(az staticwebapp secrets list -n thinkschool-quotes-swa \
  -g thinkschool-rg --query properties.apiKey -o tsv)
npx @azure/static-web-apps-cli deploy ./dist/quotes-angular/browser \
  --api-location ./api --deployment-token "$DEPLOY_TOKEN" --env production

# 6. Wire future CI: copy that deployment token into the GitHub repo as
#    AZURE_STATIC_WEB_APPS_API_TOKEN (Settings → Secrets and variables →
#    Actions), so day17-static-web-app.yml deploys on every push.

# 7. Custom domain, once one is available:
az staticwebapp hostname set -n thinkschool-quotes-swa -g thinkschool-rg \
  --hostname <your-domain>
```

## How to redeploy the live Container Apps substitute

```bash
cd Day-17/quotes-angular
npx ng build --configuration production
docker build -t thinkschoolquotesacr.azurecr.io/quotes-web:<new-tag> .
az acr login --name thinkschoolquotesacr
docker push thinkschoolquotesacr.azurecr.io/quotes-web:<new-tag>
az containerapp update -g thinkschool-rg -n quotes-web \
  --image thinkschoolquotesacr.azurecr.io/quotes-web:<new-tag>
```

(`az acr build`, the remote-build path, is itself blocked in this
subscription — `TasksOperationsNotAllowed` — so the image is built locally
with `docker build` and pushed directly, confirmed working above.)

## Verification log

### Build and test — real, run end to end this session

```
Day-17/quotes-angular$ npx ng build --configuration production
  Initial total: 288.21 kB raw / 79.42 kB transfer
  Lazy chunks: quote-detail, create-quote, login — confirmed separate

Day-17/quotes-angular$ npx ng test --watch=false   # with the real Day-5 API
  running locally on :5147 (Jwt__Key set to a local-only dev value)
  12 test files, 70/70 passing — including the characterization suite that
  talks to a real, running QuotesApi instance, not a mock

Day-5$ dotnet build QuotesApi/QuotesApi.csproj        # clean, 0 warnings
Day-5$ Jwt__Key=<local-only> dotnet test QuotesApi.Tests/QuotesApi.Tests.csproj
  Passed: 4, Failed: 0
```

### The live deployment — hit directly, over the real internet

```
$ curl -s -o /dev/null -w "%{http_code}" https://quotes-web.bravebay-90a32791.centralindia.azurecontainerapps.io/
200

$ curl -s https://quotes-web.bravebay-90a32791.centralindia.azurecontainerapps.io/api/quotes
[{"id":1,"author":"Ada Lovelace", ...}, ... 4 more ...]   # real data, from the real Week-1 API

$ curl -sI https://quotes-web.bravebay-90a32791.centralindia.azurecontainerapps.io/
HTTP/2 200
content-security-policy: default-src 'self'; script-src 'self' 'unsafe-inline'; ...
x-content-type-options: nosniff
x-frame-options: DENY

$ curl -s -o /dev/null -w "%{http_code}" https://.../quotes/1   # hard-reload deep link
200   # SPA fallback works, not a 404
```

### Zero secrets, anywhere

```
$ az containerapp show -g thinkschool-rg -n quotes-web \
    --query "properties.template.containers[0].env"
[ QUOTES_API_BASE_URL (a public FQDN), QUOTES_API_SCOPE (an audience URI) ]
# no secretRef for anything auth-related; grep of the repo finds no key,
# connection string, or certificate for calling the Week-1 API anywhere
```

### The Managed Identity token — captured and verified, not assumed

A one-time diagnostic call (via the now-removed `/__debug-token-claims`
route, used once for exactly this evidence, then deleted — see "What was
built," item 4) decoded the real token `quotes-web`'s Managed Identity
acquired, and made a direct server-side call to the real API with it:

```json
{
  "header": { "alg": "RS256", "kid": "T5h40q7G0x49qn41lM9-kKjpD98" },
  "payload": {
    "aud": "697131d5-940f-4d27-a027-8f2284907c64",
    "iss": "https://login.microsoftonline.com/8d46a076-d093-416d-a57b-8692cde13bf8/v2.0",
    "oid": "aba57120-654e-476f-80e6-683a8780843d",
    "sub": "aba57120-654e-476f-80e6-683a8780843d",
    "roles": ["Quotes.Write"],
    "ver": "2.0"
  },
  "directCallStatus": 201
}
```

`oid`/`sub` match `quotes-web`'s own system-assigned Managed Identity
principal ID exactly — this is a real Entra token issued by Azure to this
specific container, carrying the `Quotes.Write` app role, accepted by the
real API with a `201 Created`. That single request could not have succeeded
any other way: no dev token was ever presented (the local dev-token JWT is
signed with a different key than the one the deployed API validates
against, so it 401s if tried — confirmed), and `CanEditQuotes` requires
either that dev-token scope or this exact `roles` claim.

A second, non-debug proof of the same thing: a plain client `POST
/api/quotes` (no `Authorization` header at all, sent by curl) to the live
`quotes-web` URL still returned `201 Created` — because the proxy always
attaches its own Managed-Identity token regardless of what the caller sends.
This is the demo data currently visible in the live app (5 quotes, seeded
this way; the debug call's own throwaway record was soft-deleted
immediately after and is correctly filtered out by the Angular app's
`visibleQuotes`, confirmed in the screenshot below).

### Lighthouse — run against the live, deployed URL

| Category | Mobile (default, simulated slow 4G) | Desktop |
|---|---|---|
| Performance | **96** (re-run: 95) | **100** |
| Accessibility | **100** | **100** |
| Best Practices | **100** | **100** |
| SEO | **100** | **100** |

All four categories clear the ≥95 bar on both presets, against the real
deployed origin — not localhost, not an emulator. Full reports:
`Day-17/lighthouse/live-mobile.report.{json,html}` and
`live-desktop.report.{json,html}`.

This score only came together after two real fixes, not by re-running until
a good number appeared:

1. **No compression.** The first live run scored Performance 69 (mobile) —
   worse than the earlier localhost-emulator estimate of 82, because a real
   deployed origin has real network + payload cost that `localhost` never
   pays. `curl -H "Accept-Encoding: gzip, br"` against a JS chunk showed no
   `Content-Encoding` header at all — the container was shipping every
   asset uncompressed, something Static Web Apps' CDN would have handled
   for free. Adding `compression` middleware fixed it: the same request now
   returns `content-encoding: br`, and mobile Performance jumped to 96.
2. **No cache headers on hashed assets.** Added
   `Cache-Control: public, max-age=31536000, immutable` on the
   content-hashed JS/CSS output — again, something a CDN-backed static host
   gets by default and a bare container doesn't.

This is also the honest answer to "why not just use Static Web Apps" beyond
the policy block: a CDN-backed static host gets compression, caching, and
edge locality for free, and this session had to reproduce two of those three
by hand to hit the same bar on a single-region container.

Superseded, kept for the record: `Day-17/lighthouse/lighthouse-mobile.*` and
`lighthouse-desktop.*` are the original localhost-SWA-emulator run from the
earlier pass (Performance 82/99) — real at the time, but a weaker proof than
the live numbers above.

## Screenshots

- `Day-17/screenshots/day17-live-quotes.png` — the real, live, deployed app
  (`quotes-web`'s public URL), showing "API connected," 5 quotes / 5 authors
  from the real Week-1 API, rendered through the Managed-Identity proxy.
- `Day-17/screenshots/day17-swa-emulator-quotes.png` — kept from the earlier
  pass: the same app under the official SWA CLI emulator, for comparison.

## Files changed/added this session

- `Day-17/quotes-angular/server/server.js` — removed the temporary
  debug-token route (security cleanup, used once for evidence then
  deleted); added `compression` middleware; added long-lived cache headers
  on hashed static assets.
- `Day-17/quotes-angular/server/package.json` / `package-lock.json` — added
  `compression` dependency.
- `Day-17/quotes-angular/dist/` — rebuilt production bundle.
- Azure: new `quotes-web` Container App (system-assigned identity, granted
  `Quotes.Write`), redeployed twice this session (`0.1.3` removes the debug
  route, `0.1.4` adds compression/caching).
- `Day-5/QuotesApi/Program.cs` — removed the leftover `/api/debug/whoami`
  diagnostic endpoint; rebuilt and redeployed as `quotes-api:0.6.0`.
- Live Week-1 API: seeded 5 real quotes via the Managed-Identity path itself
  (no dev token used) for a real, non-empty demo — reseeded once after the
  `quotes-api:0.6.0` redeploy, since the API's ephemeral SQLite storage
  resets on every new revision.
- `Day-17/lighthouse/live-mobile.*`, `live-desktop.*` — new, live-origin
  Lighthouse reports.
- `Day-17/screenshots/day17-live-quotes.png` — new.
- `Day-17/quotes-angular/.gitignore` — added a non-anchored `node_modules/`
  rule so `server/node_modules` (a separate npm project from the Angular
  app's own) doesn't get swept into a commit; the existing anchored
  `/node_modules` rule only covered the app's own root.
- This README, rewritten to reflect the actual live state.

Untouched this session (already correct from the earlier pass): the Angular
app's `src/app` code, `staticwebapp.config.json`, the SWA Managed-Identity
Functions proxy in `api/`, the `CanEditQuotes`/audience-validation logic in
`Program.cs`, and `.github/workflows/day17-static-web-app.yml`.
