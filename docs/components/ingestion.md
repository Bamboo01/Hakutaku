# Telemetry ingestion

!!! info "Stub — no code exists yet"
    This page records what the service is *for*, so whoever picks it up does
    not have to reconstruct the intent. It used to be the Simulator page, back
    when `simulator/` was meant to hold it. That directory is now the .NET
    [Simulator](simulator.md) of mock players, and this service has no
    directory yet.

## What it is meant to be

A **C++ telemetry ingestion service**: the thing that takes event streams from
game servers and gets them into Hakutaku.

It is tracked as `TODO.md` item 7 and explicitly scoped as a **stretch goal**,
not part of the core backend. It should not block anything else.

## The decision that has not been made

There are two ways to get telemetry into the database, and the choice has real
consequences:

=== "POST to `/api/events`"

    **One source of truth.** Validation, timestamping and the schema all stay in
    the ASP.NET app. The ingestion service only has to speak HTTP and JSON.

    Slower per event — HTTP overhead, plus whatever the server does — and it
    makes the API a hard dependency of ingestion.

=== "Write straight to Postgres"

    **Faster**, and ingestion survives the API being down.

    But it duplicates validation logic in a second language, and every schema
    change now has two places to update. Nothing stops the two from drifting
    apart.

The current lean in `TODO.md` is toward the API, for the single-source-of-truth
reason. Not decided.

## What has to exist first

### 1. A stable event contract

`POST /api/events` exists and works today. Its shape:

```json
{
  "playerId": "<guid of an existing player>",
  "eventType": "level_up",
  "data": "{\"level\":2}"
}
```

`timestamp` is always set server-side in UTC; anything a client sends is
overwritten. `data` is JSON held as **text**, not `jsonb`.

See the [API reference](../reference/api.md).

### 2. Non-admin authentication

This is the blocker, and it is not small.

`/api/events` is currently gated by `.RequireAdmin()`, which means it needs an
**admin session cookie**. A game server has no business holding one — those
cookies are minted by a human typing a password, expire after 12 hours, and
carry admin privileges across the whole API.

So ingestion needs a **server-key path**: a credential issued per game server,
scoped to writing events and nothing else. The planning documents mention a
`server_keys` table; no schema for it exists.

### 3. A public route

`/api/events` is not reachable from the internet — `caddy/Caddyfile` is
default-deny and only publishes `/Health`. A real game server, which is not
inside an SSH tunnel, needs a `handle` block.

See [Caddy](caddy.md#adding-a-public-route).

## Open questions

- Is this a long-running service or a batch job?
- Does it buffer locally and retry, or drop on failure?
- One process per game server, or one aggregator for all of them?
- Does it need backpressure handling if the API is slow?
- How are server keys provisioned and rotated?

## Related reading

- [Server](server.md) — how the event endpoint is written today
- [Database](database.md) — the `TelemetryEvents` table
- [Practices](../architecture/practices.md) — why admin auth is the wrong gate
- `TODO.md` items 1 and 7
