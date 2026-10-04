# Hakutaku Simulator

Makes **mock players** and has them talk to a Hakutaku server through the SDK, the same way the Unity plugin will. Use it to check the server still behaves, or to try the player API by hand.

It has two modes:

- **`probe`** runs a scripted check and tells you if anything answered differently from what it should.
- **`shell`** gives you a prompt where you create players and call the API yourself.

The full details are on the wiki's [Simulator page](../docs/components/simulator.md).

---

## Before you start

1. Install the **.NET 10 SDK** from <https://dotnet.microsoft.com/download/dotnet/10.0>. Check it worked with `dotnet --list-sdks`: a line starting with `10.` should appear. `start.bat` doesn't need this, but the simulator does.
2. **Start Hakutaku.** The simulator needs a server to talk to. Either one works:
   - `start.bat` / `start.sh` (see the [main readme](../readme.md)). The server is at `http://localhost:8090`.
   - The dev loop, `dotnet watch` in `server/`. The server is at `http://localhost:5008`.

## Pick the server address

The simulator talks to `http://localhost:5008` unless you tell it otherwise. **If you started Hakutaku with `start.bat`, you have to point it at `http://localhost:8090`**, with `--url` on each command:

```
dotnet run -- probe --url http://localhost:8090
```

or once per terminal, with the `HAKUTAKU_URL` environment variable:

```powershell
# Windows (PowerShell)
$env:HAKUTAKU_URL = "http://localhost:8090"
```

```bash
# Mac or Linux
export HAKUTAKU_URL=http://localhost:8090
```

The examples below leave `--url` out, so add it if you need it.

## Run the probe

Open a terminal in this `simulator` folder and run:

```
dotnet run -- probe
```

The first run builds the simulator, which takes a moment. Each check prints `ok` or `FAIL` on its own line, and the last line is the tally:

```
59 as expected, 0 unexpected
```

Anything in the `unexpected` count means the server didn't answer the way it should, and the command exits with code `1`.

Add `--players 10` to make more than the default 3 mock players.

**It can look stuck. It isn't.** The server allows only 5 registrations and logins a minute per address, so the probe waits and retries when it hits that limit. Three players finish in a few seconds. More players, or two runs back to back, take a couple of minutes.

## Use the shell

```
dotnet run -- shell
```

Type `help` for every command. A typical session:

```
> new Captain Bob              make a guest player; prints its device token
player-1> name Admiral Bob     change its display name
player-1> link                 link a random email + password (both printed)
player-1> verify 818337        the emailed code, see below
player-1> players              everyone made this session
player-1> quit
```

### Getting the emailed code

Hakutaku doesn't send real email unless SMTP is set up, which it isn't locally. It writes the email into the server's log instead. Look for `Your code to verify your email address is ...`:

- **Started with `start.bat`:** in the main Hakutaku folder, run `docker compose -f compose.dev.yaml logs app` and look near the end.
- **Started with `dotnet watch`:** it's in that terminal window.

## Pointing it at the real server

Avoid this unless you mean it. **Every mock player is permanent:** nothing deletes players, so each run leaves rows in the production database.

If you do, go through the SSH tunnel described in the [deploy docs](../docs/operations/deploy.md#reaching-the-admin-ui), then use `--url http://localhost:8090`. Through the tunnel the address looks like your own machine, so the simulator won't warn you that it's production. Make sure you know which one you're talking to.
