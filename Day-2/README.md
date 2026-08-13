# Day 2

Day 2 work was applied directly to the Quotes API rather than producing a
separate standalone project, so there is no duplicate copy of the API under
this folder. The current, cumulative version of the API (including the Day 2
changes below) lives in [`Day-4/QuotesApi`](../Day-4/QuotesApi).

## What Day 2 added

- **Rich domain model for `Quote`** — moved validation out of the API layer
  and into `Quote.Create(...)`, with a private setter and a `MarkDeleted()`
  soft-delete method (commit `09e9056`, rationale in
  [`Day-4/WHY.md`](../Day-4/WHY.md)).
- **JWT authentication** — added `User`, sign-up/login endpoints, and JWT
  issuing (commit `35603cf`).
- **Refresh token authentication** — added `RefreshToken`, refresh/rotate
  endpoints, and extended `AuthService`/`IAuthService` (commit `44a7dbc`).

## Where to see the Day 2 snapshot

The exact state of the repository at the end of Day 2 is preserved in git
history and is not re-created here to avoid duplicating the whole
application:

- Branch `day2-rich-quote` (tip `09e9056`)
- Branch `day2-jwt-auth` (tip `44a7dbc`)
