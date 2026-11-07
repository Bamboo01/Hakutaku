# Unity SDK and demo

`unity/` holds two things:

| Folder | What it is |
|---|---|
| `unity/com.hakutaku.sdk/` | A Unity package with both SDKs, a settings asset and a settings window, laid out like PlayFab's |
| `unity/HakutakuDemo/` | An empty Unity 6 project with one demo screen that shows Hakutaku working |

## Run the demo

1. Start a Hakutaku server. The demo points at the local Docker stack by default:
   `docker compose -f compose.dev.yaml up -d --build` (in WSL, from the repo root).
2. In **Unity Hub**, choose **Add → Add project from disk** and pick `unity/HakutakuDemo`.
   It's made for **Unity 6000.5.4f1**. The first open takes a minute while Unity imports.
3. Make a server key on the admin **Server keys** page.
4. In Unity, open **Window → Hakutaku → Settings**, paste the key, click **Save key**,
   then **Check the server and key**. It should say `OK`.
5. Press **Play**. The demo screen builds itself in any scene.

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
