# Unity SDK and demo

`unity/` holds three things:

| Folder | What it is |
|---|---|
| `unity/com.hakutaku.sdk/` | A Unity package with both SDKs, a settings asset and a settings window, laid out like PlayFab's |
| `unity/HakutakuDemo/` | An empty Unity 6 project with one demo screen that shows Hakutaku working |
| `unity/HakutakuMirrorSample/` | A tiny real Mirror game (Unity 2022.3) with Hakutaku login and saved XP. See [the Mirror sample](#the-mirror-sample) |

## Run the demo

1. Start a Hakutaku server. The demo points at the local Docker stack by default:
   `docker compose -f compose.dev.yaml up -d --build` (in WSL, from the repo root).
2. In **Unity Hub**, choose **Add → Add project from disk** and pick `unity/HakutakuDemo`.
   It's made for **Unity 6000.5.4f1**. The first open takes a minute while Unity imports.
3. Make a server key on the admin **Server keys** page.
4. In Unity, open **Window → Hakutaku → Settings**, paste the key, click **Save key**,
   then **Check the server and key**. It should say `OK`.
5. Press **Play**. The demo screen builds itself in any scene.

!!! warning "Use 127.0.0.1, not localhost"
    Unity's `HttpClient` tries `localhost`'s IPv6 address (`::1`) and doesn't fall back to
    IPv4, while Docker publishes the stack on `127.0.0.1` only. So `http://localhost:8090`
    **times out** in Unity even though the same URL works in a browser. The presets in the
    settings window already use `127.0.0.1`.

| Button | Side | What it shows |
|---|---|---|
| Register a new guest | Player (client SDK) | A new player and its device token, saved for next time |
| Log back in | Player (client SDK) | The saved device token signs the same player in again |
| Validate ticket | Game server (server SDK) | What a Mirror authenticator does when a player joins |
| Read XP | Game server | The player's XP; a key never set counts as 0 |
| +100 XP | Game server | Writes the new total, then reads it back. The number flashes green |
| Try a fake ticket | Game server | `InvalidSessionTicket`: the game would turn this player away |

Stop and press Play again, then **Log back in**: the XP is still there, because it's
stored in Hakutaku, not the game.

## How it maps to PlayFab

| PlayFab | Hakutaku |
|---|---|
| `PlayFabSharedSettings` asset in `Resources` | `HakutakuSharedSettings` asset in `Assets/Resources`: the server URL only |
| Editor Extensions window | **Window → Hakutaku → Settings** |
| `PlayFabClientAPI` | `HakutakuClientAPI` (from `sdk/`) |
| `PlayFabServerAPI`, only with `ENABLE_PLAYFABSERVER_API` | `HakutakuServerInstanceAPI` (from `sdk-server/`), only in the editor, `UNITY_SERVER` or `HAKUTAKU_SERVER` builds |
| Developer secret key in the settings asset | Server key from `HAKUTAKU_SERVER_KEY`, or in the editor a per-user file. **Never an asset** |

## Keeping the server key out of client builds

A key in a client build would let anyone write any player's data. Three things keep it out:

- **The key is never in an asset.** In the editor it's saved to
  `UserSettings/Hakutaku/ServerKey.txt`, which is per user and gitignored. A built game server
  reads `HAKUTAKU_SERVER_KEY` from its environment, and that variable wins in the editor too.
- **The server SDK isn't in client builds.** `Hakutaku.Sdk.Server.dll` and the
  `Hakutaku.Unity.Server` assembly only exist when `UNITY_EDITOR`, `UNITY_SERVER`
  (a Dedicated Server build) or `HAKUTAKU_SERVER` is defined.
- **Code that uses it is wrapped** in `#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER`,
  as the demo does, so a client build still compiles.

A game server can also take its URL from `HAKUTAKU_URL`, so the homelab can point it
anywhere without a rebuild.

## Using it in a game

Add the package to the game's `Packages/manifest.json`, with a path to wherever the
repo is checked out:

```json
"com.hakutaku.sdk": "file:../../Hakutaku/unity/com.hakutaku.sdk"
```

It pulls in Unity's Newtonsoft package by itself. Then:

- Open **Window → Hakutaku → Settings** and click **Create settings asset**.
- Copy `unity/HakutakuDemo/Assets/link.xml` into the game's `Assets`. Without it, IL2CPP
  builds strip the classes the SDKs read JSON into.
- On the game server: `var server = HakutakuServerSettings.CreateApi();`

## The Mirror sample

`unity/HakutakuMirrorSample/` proves the whole flow on a real Mirror server and clients before it goes into
a game. It's Unity **2022.3.62f3** with Mirror 96.0.1 (only Core, Components, the editor tools and KCP). Its login,
authenticator and XP code is ported from Sundered Frames' `feature/hakutaku-integration`.

| File in `Assets/MirrorSample/` | Job |
|---|---|
| `SamplePlayerLogin.cs` | Client: device login, with a token per server and `-profile` |
| `SampleAuthenticator.*.cs` | Checks the ticket and loads XP (2 s limit), rejects a bad ticket, joins offline on any other failure, kicks a duplicate login |
| `SampleXpSaver.cs` | Writes totals off the tick: one request per player, newest wins, at most every 30 s, always on leave and match end |
| `SamplePlayerXp.cs` | `SamplePlayerXp.Award(conn, amount)`, what gameplay code calls. The sample has no XP rules, only a test button |
| `SampleNetworkManager.cs`, `SampleApp.cs` | The Mirror setup and the test screen |

**To try it:** start Hakutaku locally (`docker compose -f compose.dev.yaml up -d --build`) and make a server key.
Open the project in Unity 2022.3.62f3, save the key in **Window → Hakutaku → Settings**, then choose **Tools →
Hakutaku Sample → Build test player**. In `Builds/Windows`, run the server and a client in two terminals:

```powershell
$env:HAKUTAKU_SERVER_KEY = "<key>"; .\HakutakuMirrorSample.exe -sample-server
.\HakutakuMirrorSample.exe -sample-client -profile alice
```

!!! warning "This build contains the server SDK"
    It defines `HAKUTAKU_SERVER` so one executable can be either side. It's for testing: never give it to players.

**Checked** on the local Docker stack with a real server and clients: XP survives a reconnect; logging in twice
leaves one connection and keeps the in-memory XP; with Hakutaku stopped, joins work and stored XP is unchanged;
and when Hakutaku goes down mid-match, the final stored XP matches the server's total once it's back.
Not covered: a real network cut between a game client and the server, or running on a remote server.

## Changing the SDKs

Unity uses the **DLLs** in `com.hakutaku.sdk/Plugins/`, not the source in `sdk/` and
`sdk-server/`. After changing either SDK, rebuild them and commit them with the change:

```bash
bash unity/build-sdk.sh
```

Every file in the package and the demo's `Assets` needs a `.meta` file. They're committed,
so GUIDs match on every machine. After adding a file, make its meta with:

```bash
bash unity/make-metas.sh
```

## How it was checked

Unity can't run in batch mode on the machine this was built on (no licence), so the
package and demo were compiled with `dotnet` against Unity 6.5's own engine assemblies,
in the same assembly split Unity uses. They compile with no warnings as the editor
sees them, as a client build (no server SDK) and as a Dedicated Server build. Opening
the project in Unity and pressing Play is still the real test.

## Not done yet

- **WebGL**: the SDKs use `HttpClient`, which WebGL doesn't support. It needs a
  `UnityWebRequest` version of `HakutakuHttp.cs`.
- **A published package** (a registry or git URL). For now it's a local path.
- **A Unity version of the simulator**, if one is wanted. The .NET simulator in
  `simulator/` still covers every route.
