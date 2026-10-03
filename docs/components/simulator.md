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
```

`--url` defaults to `$HAKUTAKU_URL` if that is set, otherwise
`http://localhost:5008`.

## What `probe` checks

| Step | Expected |
|---|---|
| Register N new devices, each with a display name | `200` each, a new player id, the name it asked for |
| Register player 1's device again, with a different name | `200`, **the same** player id, and the **original** name |
| Rename player 1 (with padding spaces) | `200`, stored trimmed |
| Player 2 takes player 1's name | `200`, since names aren't unique |
| Names too short, too long, or with a control character | `400` each |
| Link an email to player 1 | `200`, unverified |
| Link a second email to player 1 | `409` |
| Player 2 links player 1's email | `409` |
| Verify with a wrong code | `400` |
| Email login | `200`, the same player id as the device, with the new name |
| Email login, wrong password / unknown email | `401` both, with the same message |
| Forgot password | `200` (always, so it can't be used to look up accounts) |
| Reset with a made-up code | `400` |
| `link/email` with no token, and with a made-up token; `display-name` with no token | `401` |
| Every player: events, characters, `/api/players`, `/api/admin/*` | **denied** |

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

`register`, `login`, and the four mail routes together each allow **5 calls a
minute per IP**. The server sends no `Retry-After` header, so when the probe
gets a `429` it waits 10 seconds and tries again, for up to about 70 seconds.
Three players finish in a few seconds. Six players, or two runs back to back,
take a couple of minutes. That is the limiter working, not a hang.

## The shell

```text
> new Captain Bob              register a player with a random device ID (the name is optional)
player-1> name Admiral Bob     change the display name
player-1> link                 link a random email + password (printed)
player-1> verify 845546        the code, from the server log
player-1> forgot sim-…@example.com
player-1> reset sim-…@example.com 123456 a-new-password
player-1> login sim-…@example.com a-new-password     a second "device" for the same player
player-2> event level_up level=2 zone=forest         WritePlayerEvent, values typed as numbers/bools/strings
player-2> raw GET /api/players                       any route, with this player's token
player-2> probe                                      the access checks, as this player
player-2> players                                    everyone made this session
player-2> device 07054d4b-…                          log in again with a device ID you already have
```

`help` lists every command. `new` takes a display name and `device` takes a
device ID. They are separate commands because the server accepts any string
as a device ID, so a single command couldn't tell which one you meant.

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
| `MockPlayer.cs` | One mock player: its own `HakutakuClientInstanceAPI` (so its own session), device ID, and email and password once linked |
| `Probe.cs` | The scripted run, the `AccessChecks` table, the `429` retry |
| `Report.cs` | Prints each check as `ok` / `FAIL` and keeps the tally |
| `Shell.cs` | The interactive commands |

## Related reading

- [SDK](sdk.md) — the library every call goes through
- [Auth and sessions](auth.md) — what the player routes do on the server side
- [API reference](../reference/api.md)
