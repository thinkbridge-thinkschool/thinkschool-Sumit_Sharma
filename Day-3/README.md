# Day 3

Day 3 work continued directly on the same Quotes API from Day 2, so there is
no duplicate copy of the API under this folder. The current, cumulative
version of the API (including the Day 3 changes below) lives in
[`Day-4/QuotesApi`](../Day-4/QuotesApi), with its test suites in
[`Day-4/QuotesApi.Tests`](../Day-4/QuotesApi.Tests),
[`Day-4/Quotes.Tests.Unit`](../Day-4/Quotes.Tests.Unit),
[`Day-4/Quotes.Tests.Integration`](../Day-4/Quotes.Tests.Integration), and
[`Day-4/Tests.Domain`](../Day-4/Tests.Domain).

## What Day 3 added

- **Entra ID authentication** alongside the existing JWT scheme (commit `b81675e`).
- **Authorization policies and ownership checks** for collections (commit `3a8942d`).
- **API authorization lockdown and initial CI workflow** (commit `9e70ef3`).
- **xUnit unit test coverage** (commit `d692420`).
- **WebApplicationFactory integration tests** (commit `4783023`).
- **Integration tests against a real SQL Server Testcontainer** (commit `a127cf8`).
- **CI coverage reporting and the 70% coverage gate** (commit `e342b45`).
- **Auth coverage improvements and extracted JWT logic** (commit `b8cb6f2`).
- **Serilog structured logging with correlation IDs** (commit `7936796`).

## Where to see the Day 3 snapshot

The exact state of the repository at the end of Day 3 is preserved in git
history and is not re-created here to avoid duplicating the whole
application:

- Branch `day3-entra-id` (tip `7936796`)
