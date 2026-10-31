# Tests

`server.Tests/` is an xUnit project for the server. It has two kinds of test:

- **Unit tests** call the pure helpers directly. `PlayerInputRulesTests` covers
  how Gmail aliases fold into one key, what counts as an email, and which
  display names are refused. They need nothing running.
- **Integration tests** start the real server in memory and call it over HTTP.
  They cover the Gmail alias behaviour, the daily cap on mailed codes and the
  per-player display name limit.

## Running them

```bash
docker compose -f compose.dev.yaml up -d postgres           # the integration tests need this
dotnet test server.Tests                                    # everything
dotnet test server.Tests --filter "Category!=Integration"   # unit tests only, no database
```

Run `dotnet test` from the repo root. The integration tests use the Postgres from
`compose.dev.yaml` by default; set `HAKUTAKU_TEST_PG` to a connection string
without a database name (for example `Host=...;Username=...;Password=...`) to use
another server. If no Postgres is reachable, the first integration test fails
with a message saying so.

Jenkins does not run them yet (see [Deploy and CI/CD](../operations/deploy.md)).

## How the integration tests work

| Piece | What it does |
|---|---|
| `HakutakuFactory` | Starts the server through `WebApplicationFactory`. It makes its own empty database (`hk_test_<guid>`), so the server runs its migrations like on a fresh start, and drops it afterwards. Each test class gets its own server and database. |
| `CapturingEmailSender` | Replaces the mail sender, so a test can read the 6-digit code from a mail, or check that no mail was sent. |
| `ApiClient` | Calls the server. Every call comes from a fresh made-up IP through `X-Forwarded-For`, like behind Caddy, so the per-IP rate limits don't interfere. A test passes an IP on purpose when it wants to share one. |

A test sets up a state by writing to the database directly. For example, the
code-cap tests move the codes they made back in time instead of waiting a minute
between requests.

## Checking that a test can fail

When a test is added or changed, break the code it covers on purpose and check
the test goes red, then put the code back. We did this for the Gmail key, the
daily code cap, the display name limiter and the login lookup, and each one was
caught.

## Not covered

- The transaction rollbacks. They were checked by planting database failures, but
  that isn't saved as a test yet.
- The SQL in the `CanonicalizeGmailSubjects` migration.
- `web/`, and the admin routes beyond what the simulator's probe touches.

The simulator's probe is a separate check that runs against a live server. See
[Simulator](simulator.md).
