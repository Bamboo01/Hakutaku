# Simulator

A .NET 10 console app in `simulator/` that makes **mock players** and has them
call the server through the [SDK](sdk.md), exactly the way the Unity plugin
will. It does two jobs:

- **`probe`**, a scripted run that checks the server still behaves as
  documented. It registers players, takes one through the account flows, and
  has every player try each route beyond player auth. It exits `1` if anything
  answered differently from what it should.
- **`shell`**, an interactive prompt for poking at the API by hand as one or
  more mock players. This is the only way to finish the email-verification and
  password-reset flows, because they need the emailed code.

!!! note "Not the C++ ingestion service"
    This directory was once earmarked for the C++ telemetry ingestion service.
    That plan now has its own page, [Telemetry ingestion](ingestion.md).

## Running it

It needs a running server. The [local dev loop](../setup.md) is enough:
Postgres in Docker, then `dotnet watch` in `server/`.

```bash
cd simulator
dotnet run -- probe                    # 3 mock players against http://localhost:5008
dotnet run -- probe --players 10
dotnet run -- shell
dotnet run -- probe --url http://localhost:8090   # full Docker stack, or prod over the SSH tunnel
dotnet run -- probe --admin-password <owner password>   # also checks the server keys
```

`--url` defaults to `$HAKUTAKU_URL` if that is set, otherwise
`http://localhost:5008`.

`--admin-password` is the owner's password (the username is `admin`), or set
`$HAKUTAKU_ADMIN_PASSWORD`. With it, the probe also makes server keys and checks
them (see below). Without it, that part is skipped and the probe says so.

### Against production: keys given on the command line

On the public domain the probe can't sign in as the owner, because `/api/admin`
is tunnel-only. So it can't make keys either. Make them on the keys page instead
and hand them in:

```bash
export HAKUTAKU_SERVER_KEY=<the live key>        # or --server-key, which lands in your shell history
dotnet run -- probe --url https://51.79.242.169.nip.io \
  --revoked-key <a key you revoked> --expired-key <a key past its expiry>
```

| Step (`GivenKeyChecks.cs`) | Expected |
|---|---|
| `/api/server/ping` and `sessions/validate` with no key | `401` each |
| `ping` with the revoked key, and with the expired key | `401` each (skipped, with a note, if not given) |
| `ping` with the live key | `200` |
| The live key on `/api/admin/me` and `/api/admin/server-keys` | denied (`401`, or Caddy's `404` on the public domain) |
| Then the server SDK checks below: validate a fresh player's real ticket, write `Xp` and read it back | as below |

A player ticket on `/api/server/*` (including `sessions/validate`) is in the
`AccessChecks` table, which runs every time. Every mock player the probe makes on
production stays in that database for good. The probe removes the `Xp` it wrote.

To make an expired key: generate one on the keys page with an expiry a minute
ahead, and wait. A revoked key is any key you then revoke on the same page. Both
are useless once refused, so they're safe to pass on the command line.

## What `probe` checks

| Step | Expected |
|---|---|
| Register N guests, each with a display name | `200` each, a new player id, the name it asked for, and a device token |
| Player 1 logs in with its device token | `200`, **the same** player id |
| A made-up device token | `401` |
| Register player 1's hardware ID again | `200`, a **different** player: the hardware ID is only recorded |
| An old client's `register` body (`deviceId`, no `hardwareId`) | `400` |
| Rename player 1 (with padding spaces) | `200`, stored trimmed |
| Player 2 takes player 1's name | `200`, since names aren't unique |
| Names too short, too long, or with a control character | `400` each |
| Player 1's 5th and 6th name tries in the hour | `200`, then `429` (the limit is per player) |
| Link an email to player 1 | `200`, unverified |
| Link a second email to player 1 | `409` |
| Player 2 links player 1's email | `200`, the same answer as a free email, so it can't be used to look up accounts |
| Player 1's device token, with the email linked but not verified | `200`: it's only removed at verification |
| Verify with a wrong code | `400` |
| Email login | `200`, the same player id as the device, with the new name |
| Email login, wrong password / unknown email | `401` both, with the same message |
| Forgot password | `200` (always, so it can't be used to look up accounts) |
| Reset with a made-up code | `400` |
| `link/email` with no token, and with a made-up token; `display-name` with no token | `401` |
| Every player: events, characters, `/api/players`, `/api/admin/*` (server keys included), `/api/server/ping`, and `/api/server/players/{id}/data` | **denied**. A player's own id and token don't open their own data: that takes a server key |

With the owner's password, the probe also signs in as `admin` and checks the
server keys (`ServerKeyChecks.cs`, over plain HTTP because the SDK has no admin
calls):

| Step | Expected |
|---|---|
| Make a key | `200`, a 43-character key |
| `/api/server/ping` with no key, and with a wrong key | `401` each |
| `/api/server/ping` with the fresh key | `200` |
| A server key on `/api/admin/server-keys`, with no cookie | `401` |
| Make a second key, check it works, revoke it, check it again | `200`, `204`, then `401` |
| Make a key that expires in 3 seconds, wait 4, check it | `200`, then `401` |
| The key list | `200`, and no key in it |

With that same fresh key, the probe then acts as a game server on the first mock
player (`PlayerDataChecks.cs`):

| Step | Expected |
|---|---|
| Write `Xp` = 1200 in the ReadOnly tier, then read it back | `200`, the same value |
| The same write again, then write 1300 | still 1200, then 1300: a write sets, it never adds |
| Read `Xp,Nope`, read with no keys, read `Xp` in another tier | only `Xp`, every key, nothing |
| A read with no tier, and with `tier=1` | `400` each |
| A key with a space, a 65-character key, a 16385-byte value, 11 changes, a key both set and removed | `400` each, and nothing changed |
| 100 keys in the Internal tier (10 requests of 10), then a 101st | `200`s, then `400` |
| Remove those 100 keys again | none left |
| An unknown player, read and write | `404` each |

Then, with the same key, the probe plays a game server's authenticator through
the **server SDK** (`ServerSdkChecks.cs`, using `sdk-server/` as a game server
would):

| Step | Expected |
|---|---|
| A fresh mock player registers through the client SDK, and `AuthenticateSessionTicket` validates its ticket | the same player id |
| `AuthenticateSessionTicket` with a made-up ticket | `InvalidSessionTicket`, not unavailable |
| The real ticket with a wrong server key | `ServerKeyRejected`, never the ticket's fault |
| `UpdateUserReadOnlyData` `Xp` = 1200, then `GetUserReadOnlyData` `Xp,NotSet` | updated 1, then `Xp` = 1200 and no `NotSet` |
| A local socket that accepts and never answers, with a 1-second timeout | `Timeout`, and `IsHakutakuUnavailable` |
| A port with nothing listening | `ConnectionError`, and `IsHakutakuUnavailable` |

The probe revokes every key it made, so it leaves nothing usable behind. It does
leave the (revoked) rows, since no route deletes a key. It removes the `Xp` and
the 100 keys it wrote, so the mock player is left with no data.

The probe can't check that **verifying removes the device token**, because that
needs the real mailed code. Do it in the shell: `new`, `link`, `verify <code>`,
then `device <token>` should get a `401` while `login <email> <password>` still
works.

"Denied" means `401` or `403`, which is what the admin gate answers locally
and over the SSH tunnel. When `--url` is anything other than this machine, a
`404` counts too: on the public domain, Caddy answers `404` for a non-public
route before the request ever reaches the app. Locally, `404` deliberately
**fails**. The app also answers `404` for a route that doesn't exist, so a
renamed route (the planned `/api/players` → `/api/player`, say) would otherwise
pass as "denied" while nothing was checking the new one.

The list of non-player routes and their expected answers is the
`AccessChecks` table at the top of `simulator/Probe.cs`. **When the server
opens a route to player tokens, change that route's `Expect` there**, so the
probe keeps checking the new rule rather than flagging it as a failure.

### Rate limits make it slow, on purpose

`register`, `login/device`, `login/email`, and the four mail routes together
each allow **5 calls a minute per IP**. A default 3-player run uses all 5 of
`register`'s. The server sends no `Retry-After` header, so when the probe
gets a `429` it waits 10 seconds and tries again, for up to about 70 seconds.
Three players finish in a few seconds. Six players, or two runs back to back,
take a couple of minutes. That is the limiter working, not a hang.

## The shell

```text
> new Captain Bob              register a guest (the name is optional); prints its device token
player-1> name Admiral Bob     change the display name
player-1> link                 link a random email + password (printed)
player-1> verify 845546        the code, from the server log; this removes the device token
player-1> forgot sim-…@example.com
player-1> reset sim-…@example.com 123456 a-new-password
player-1> login sim-…@example.com a-new-password     a second "device" for the same player
player-2> event level_up level=2 zone=forest         WritePlayerEvent, values typed as numbers/bools/strings
player-2> raw GET /api/players                       any route, with this player's token
player-2> probe                                      the access checks, as this player
player-2> players                                    everyone made this session
player-2> device DeQrM3…                             log in again with a device token from an earlier 'new'
```

`help` lists every command. Each mock player gets its own made-up hardware ID,
which the server only records.

**Getting the code.** With no `SMTP_HOST` configured, which is the case locally
and in production today, the server doesn't send mail. It logs the message
instead, as a warning from `server.LogEmailSender`:

```text
warn: server.LogEmailSender[0]
      No SMTP server configured, so this email was not sent. To: sim-…@example.com, Subject: Hakutaku: Verify your email
Your code to verify your email address is 845546.
```

Mock emails use `@example.com`, a reserved domain that accepts no mail, so a
server that does have SMTP configured never delivers them anywhere.

## Pointing it at production

You can, with two things to know:

- **Over the public domain** only the player-auth routes are reachable, so
  everything else comes back `404`, which counts as denied. Over the
  [SSH tunnel](../operations/deploy.md#reaching-the-admin-ui) you see the real
  `401`s.
- **Every mock player is permanent.** There is no route that deletes a player,
  so each run leaves rows in the production database. The simulator prints a
  warning whenever `--url` isn't this machine.

## Layout

| File | What it holds |
|---|---|
| `Program.cs` | Argument parsing, and the dispatch to `probe` or `shell` |
| `MockPlayer.cs` | One mock player: its own `HakutakuClientInstanceAPI` (so its own session), a made-up hardware ID, its device token, and email and password once linked |
| `Probe.cs` | The scripted run, the `AccessChecks` table, the `429` retry |
| `ServerKeyChecks.cs` | With the owner's password: makes, uses, revokes and expires server keys, then hands a fresh key to the two below |
| `PlayerDataChecks.cs` | The player data routes over plain HTTP, as a game server |
| `ServerSdkChecks.cs` | The same job through the server SDK (`sdk-server/`): validate a ticket, write and read `Xp`, and the error split |
| `GivenKeyChecks.cs` | With `--server-key`, `--revoked-key` or `--expired-key`: the refusals and XP end to end, for production where the probe can't make keys |
| `Report.cs` | Prints each check as `ok` / `FAIL` and keeps the tally |
| `Shell.cs` | The interactive commands |

## Related reading

- [SDK](sdk.md) — the library every call goes through
- [Auth and sessions](auth.md) — what the player routes do on the server side
- [API reference](../reference/api.md)
