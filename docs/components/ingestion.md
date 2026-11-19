# Telemetry ingestion

`ingest/` is **hakutaku-ingest**, a small C++20 service that game servers send
telemetry events to. It answers at once and gets the events into Hakutaku in
batches, so a game server never waits on the database and a burst of events
costs a handful of inserts instead of one HTTP call each.

```text
game server ──POST /ingest/events──> caddy ──> ingest:9000 ──POST /api/server/events/batch──> app ──> postgres
  (X-Server-Key: its own key)                  (queue, batches,   (X-Server-Key: ingest's key)
                                                 retries)
```

It **writes through the API**, not straight to Postgres. That was the open
decision on this page; the API won because every rule about an event (sizes,
JSON, which players exist) then lives in one place, `server/Services/EventRules.cs`,
and a schema change never needs a C++ change.

## What a game server does

```bash
curl -X POST https://51.79.242.169.nip.io/ingest/events \
  -H "X-Server-Key: <the game server's key>" \
  -H "Content-Type: application/json" \
  -d '{ "events": [
        { "playerId": "<player id>", "eventType": "level_up",  "data": { "level": 3 } },
        { "playerId": "<player id>", "eventType": "match_end", "data": { "won": true, "kills": 7 } }
      ] }'
# 202 {"queued":2,"rejected":[]}
```

- The body is one event, or `{ "events": [...] }` with 1 to 1,000 of them (4 MB at most).
- `data` is any JSON value, or a string that holds JSON. It is stored as JSON text,
  at most 10,000 bytes.
- `eventType` is 1-100 characters with no control characters. `playerId` is a player's GUID.
- The time stored is when **ingest received** the event, sent on as `occurredAt`, so
  it stays right even if delivery is delayed. A time from the game is never used.

| Status | When |
|---|---|
| `202` | Taken. `queued` events will be delivered; `rejected` lists the bad ones by `index` with the same messages the server uses |
| `400` | Not JSON, or not 1-1,000 events |
| `401` | No `X-Server-Key`, or not a live server key |
| `413` | The body is over 4 MB |
| `503` | The queue is full (Hakutaku has been down a while) or the key can't be checked right now. Retry after `Retry-After` seconds. Nothing from that request was queued, so retrying can't double it |

A `202` means *queued*, not *stored*. Whether the player exists is checked by
Hakutaku when the batch arrives; an event for an unknown player is refused there,
counted in `/ingest/health` as `refusedByHakutaku`, and logged.

## How it works

| File | Job |
|---|---|
| `src/main.cpp` | The HTTP server (cpp-httplib), the two routes, startup and shutdown |
| `src/config.*` | Settings from environment variables; a bad value stops it at startup |
| `src/event.*` | Checks one event like `EventRules.cs` does, and stamps the time |
| `src/event_queue.hpp` | The bounded, thread-safe queue between the HTTP threads and the forwarder |
| `src/key_check.*` | Asks Hakutaku whether a game server's key is live, and remembers briefly |
| `src/forwarder.*` | The one thread that sends batches, with retry and backoff |

- **Keys.** A game server uses its own server key. Only Hakutaku knows whether a key
  is live, so `KeyCheck` asks it (`GET /api/server/ping`) and remembers a good answer
  for 60 seconds and a bad one for 10 (at most 1,000 remembered). A revoked key stops
  working here within a minute. If Hakutaku can't be asked, the request gets a `503`:
  an unchecked key is never let in. Ingest sends its batches with **its own** key,
  `INGEST_SERVER_KEY`.
- **Batching.** A batch goes as soon as it has 500 events (the most Hakutaku takes in
  one call) or a second after the first one waited, whichever is sooner.
- **Backpressure.** The queue holds 100,000 events. If Hakutaku is down that long,
  new requests get a `503` instead of the service growing until it runs out of
  memory. A request is queued whole or not at all.
- **Retries.** No answer, a `5xx`, a `429` or a `401` (ingest's own key revoked) keeps
  the batch and tries again after 0.5 s, 1 s, 2 s... up to 30 s; `/ingest/health`
  shows `"status": "backing_off"`. A `400` means the batch itself is bad, which a
  retry can't fix, so it is dropped and logged.
- **At least once.** If Hakutaku stored a batch but the answer was lost, the retry
  stores it again. Rare duplicates are possible; lost events are not, short of the
  process dying.
- **Shutdown.** On `SIGTERM` (`docker stop`) it stops taking requests and spends up to
  10 seconds delivering what's queued. Whatever is left after that is counted as
  `lost` and logged.

## Health

`GET /ingest/health` is not published by Caddy. Locally:

```bash
curl http://127.0.0.1:9000/ingest/health
```

```json
{ "status": "ok", "queued": 0, "queueCapacity": 100000, "received": 20501,
  "refusedOnArrival": 2, "stored": 20499, "refusedByHakutaku": 0,
  "batchesSent": 42, "retries": 0, "lost": 0, "lastProblem": "" }
```

## Settings

| Variable | Default | Meaning |
|---|---|---|
| `HAKUTAKU_SERVER_KEY` | (required) | Ingest's own server key; compose fills it from `INGEST_SERVER_KEY` |
| `HAKUTAKU_URL` | `http://localhost:5008` | Where the app is; compose sets `http://app:8080` |
| `INGEST_PORT` | `9000` | |
| `INGEST_BATCH_SIZE` | `500` | 1-500 |
| `INGEST_FLUSH_MS` | `1000` | Longest an event waits for a batch to fill |
| `INGEST_QUEUE_MAX` | `100000` | Events held while Hakutaku is unreachable |

## Running it

**In Docker** (what production does): make a server key called `ingest` on the
admin UI's Server keys page, then

```bash
INGEST_SERVER_KEY=<key> docker compose -f compose.dev.yaml up -d --build ingest
```

Locally it is on `127.0.0.1:9000`, and through Caddy at `http://127.0.0.1/ingest/events`.
Without a key the container exits with a message saying so and Docker keeps
retrying; the rest of the stack is unaffected.

**Building by hand** (WSL or Linux, g++ 12+ and CMake 3.24+). The dependencies are
fetched by CMake at pinned versions:

```bash
cd ingest
cmake -S . -B build && cmake --build build -j
./build/ingest_tests                                    # unit tests (doctest)
HAKUTAKU_SERVER_KEY=<key> ./build/hakutaku-ingest       # against dotnet watch on :5008
```

The Docker build runs the unit tests too, so a failing test stops the image, and
with it a Jenkins deploy. They also pass under ThreadSanitizer:

```bash
cmake -S . -B build-tsan -DCMAKE_BUILD_TYPE=Debug -DCMAKE_CXX_FLAGS="-fsanitize=thread -g"
cmake --build build-tsan --target ingest_tests && setarch $(uname -m) -R ./build-tsan/ingest_tests
```

(`setarch -R` turns off address randomisation for that one run, which newer kernels
need for ThreadSanitizer to start.)

## Measured

On the local dev stack (WSL, everything in Docker): 40 requests of 500 events from 8
parallel senders were all accepted in 0.07 s, and all 20,000 were stored in
Postgres 1.8 s after the first was sent, about 11,000 events a second end to end,
with nothing lost. Stopping the app for 10 seconds lost nothing either: events kept
being accepted and were stored, with their original times, once it was back.

## Going live

1. Merge (Jenkins builds the `ingest` image with the rest).
2. On the Server keys page, make a key called `ingest`. Add it to the VM's `.env` and
   to Jenkins' copy (`/var/lib/jenkins/hakutaku.env`) as `INGEST_SERVER_KEY`, then
   `docker compose -f compose.yaml up -d ingest` (or let the next deploy do it).
3. Point the game server at `https://51.79.242.169.nip.io/ingest/events` with its own key.

## Not done yet

- A `WriteTelemetryEvents` method in the server SDK (`sdk-server/`), so the game
  server doesn't hand-write the HTTP call.
- Events straight from game **clients** with a player token. Today only servers can send.
- An idempotency key per event, to remove the rare duplicate after a lost answer.
- Running the C++ tests as their own Jenkins stage (today they only run inside the
  Docker build).

## Related reading

- [Server](server.md): how the event routes are written
- [Database](database.md): the `TelemetryEvents` table
- [Auth and sessions](auth.md#server-keys): server keys
- [Caddy](caddy.md): why only `/ingest/events` is public
