# SDK

A C# client library in `sdk/`, shaped like **PlayFab's client SDK**, so a game
(the Fabled plugin) never hand-rolls HTTP calls or JSON against this API. It
stands in for the future Unity SDK. It is written so it can go into a Unity
project unchanged, and the [Simulator](simulator.md) is its first user.

## Using it

Like PlayFab, there are two ways in.

**The static API**, one player per process. This is what a game uses, and the
callback form is what Unity code expects:

```csharp
using Hakutaku;
using Hakutaku.ClientModels;

HakutakuSettings.staticSettings.ServerUrl = "https://51.79.242.169.nip.io";

HakutakuClientAPI.LoginWithDeviceID(
    new LoginWithDeviceIDRequest { DeviceId = SystemInfo.deviceUniqueIdentifier },
    result => Debug.Log("Logged in as " + result.PlayerId),
    error => Debug.LogError(error.GenerateErrorReport()));
```

**An instance per player**, for anything that holds several players at once,
such as the simulator or a dedicated server. Every call also has an `…Async`
form that returns a result instead of taking callbacks:

```csharp
var client = new HakutakuClientInstanceAPI(new HakutakuApiSettings { ServerUrl = "http://localhost:5008" });

var login = await client.LoginWithDeviceIDAsync(new LoginWithDeviceIDRequest { DeviceId = deviceId });
if (login.Error != null)
    Console.WriteLine(login.Error.GenerateErrorReport());
else
    Console.WriteLine(login.Result.PlayerId);
```

No call throws on an HTTP or network failure. Every result has exactly one of
`Result` and `Error` set.

## The calls

| SDK call | PlayFab counterpart | Route | Works with a player token today? |
|---|---|---|---|
| `LoginWithDeviceID` | `LoginWithCustomID` | `POST /api/players/register` | Yes (public) |
| `LoginWithEmailAddress` | same | `POST /api/players/login` | Yes (public) |
| `UpdateUserTitleDisplayName` | same | `POST /api/players/display-name` | Yes |
| `LinkEmailAddress` | `AddUsernamePassword` | `POST /api/players/link/email` | Yes |
| `VerifyEmail` | — | `POST /api/players/email/verify` | Yes |
| `ResendVerificationEmail` | — | `POST /api/players/email/resend` | Yes |
| `SendAccountRecoveryEmail` | same | `POST /api/players/password/forgot` | Yes (public) |
| `ResetPassword` | — | `POST /api/players/password/reset` | Yes (public) |
| `WritePlayerEvent` | same | `POST /api/events` | **No** — admin-only, `401` |
| `GetAllUsersCharacters` | same | `GET /api/characters` | **No** — admin-only, `401` |
| `GrantCharacterToUser` | same, minus the catalog item | `POST /api/characters` | **No** — admin-only, `401` |

The last three exist so game code can be written against them now. They start
working when the server opens those routes to player tokens
(`TODO.md` item 2), and their wire format will likely change at that point.
That change belongs in `ServerContract.cs` (below), not in game code.

Differences from PlayFab worth knowing:

- **No `TitleId`.** Hakutaku is self-hosted and serves one game, so settings
  hold a `ServerUrl` instead.
- **No `CreateAccount` flag** on device login: `register` always
  finds-or-creates.
- **Device login can set the display name**: `LoginWithDeviceIDRequest.DisplayName`
  is optional and used only when the call creates the player. A known device
  keeps its name, and `LoginResult.DisplayName` shows what stuck (null for none).
  `UpdateUserTitleDisplayName` changes it any time. Names are 3–25 characters and
  **not unique**, so never use one to identify a player.
- **`ForgetAllCredentials` is client-side only.** There is no player logout
  route, so the token stays valid on the server until it expires (30 days) or a
  password reset revokes it.
- `SendRawRequestAsync(method, path, json)` sends any request with the current
  token and returns the body as text. The simulator uses it to check that admin
  routes refuse player tokens. A game shouldn't need it.

## Errors

`HakutakuError` mirrors `PlayFabError`: `ApiEndpoint`, `HttpCode`,
`HttpStatus`, `Error` (a `HakutakuErrorCode`), `ErrorMessage`, and
`GenerateErrorReport()`.

The server sends only `{ "error": "<text>" }`, with no machine-readable code,
so `HakutakuErrorCode` comes from the HTTP status: `InvalidParams` (400),
`NotAuthenticated` (401), `Conflict` (409), `TooManyRequests` (429), and so on.
Two codes mean nothing was answered: `NotLoggedIn`, when a session call was
made before logging in and was never sent, and `ConnectionError`, when no
response came back.

The SDK also deals with the responses that aren't `{ error }`:

- the rate limiter's `429` has an empty body;
- Caddy's `404` for a non-public route has an empty body;
- a bad id can still produce a bare `500`.

These get a sensible default message instead of a JSON parse failure.

!!! tip "If the server ever sends error codes"
    Mapping on message text would break the first time a message is reworded.
    A stable `code` field in the server's error body is the fix, and the SDK
    would then read it in `HakutakuHttp.MessageFor`.

## How it's built

| File | What it holds |
|---|---|
| `HakutakuClientAPI.cs` | The static facade over one default instance |
| `HakutakuClientInstanceAPI.cs` | Every call, in `Async` and callback forms, plus the mapping from wire to public types |
| `ClientModels.cs` | The public request and result types (`Hakutaku.ClientModels`) |
| `ServerContract.cs` | **Internal.** The routes and the server's JSON shapes, kept in one place |
| `HakutakuHttp.cs` | **Internal.** The only file that touches the network or JSON |
| `HakutakuSettings.cs` | `HakutakuApiSettings`, `HakutakuAuthenticationContext`, and the static defaults |
| `HakutakuError.cs` | `HakutakuError`, `HakutakuErrorCode`, `HakutakuResult<T>` |

Decisions that shape it:

- **Instance-first.** Each `HakutakuClientInstanceAPI` owns its session, like
  PlayFab's instance API, so mock players never share a token. One `HttpClient`
  is shared by all of them. The Bearer token goes on each request rather than
  in `DefaultRequestHeaders`, and cookies are off.
- **Public types are separate from the wire format.** The server's JSON is
  mapped into PlayFab-style types in one place. A route rename such as the
  planned `/api/players` → `/api/player` is then an internal edit, not a
  breaking change for games.
- **IDs are opaque strings.** `PlayerId`, `CharacterId` and `EventId` are never
  parsed as GUIDs; see [Practices](../architecture/practices.md#treat-ids-as-opaque).
- **Callbacks run on the caller's thread.** The callback overloads await the
  async call on the caller's `SynchronizationContext`, so in Unity they fire
  on the main thread, as PlayFab's do.

## Taking it into Unity

The project targets **`netstandard2.1` with C# 9**, which is what Unity 2021.2
and later compile. Keep it that way: no file-scoped namespaces, records,
`init`, `required` or global usings in `sdk/`. The project file pins
`LangVersion` so the build catches them.

Nullable reference types are switched on by a `#nullable enable` line at the
top of each file, not in the project file, because Unity compiles copied
source without ever reading the `.csproj`. **Start every new SDK file with that
line.** If you forget, the build warns on the file's first `?`.

- **JSON is Newtonsoft.Json.** Unity ships it as an official package
  (`com.unity.nuget.newtonsoft-json`). System.Text.Json would have to be
  vendored DLL by DLL.
- **HTTP is `HttpClient`**, which works in the editor and on desktop and mobile
  builds but **not on WebGL**. For WebGL, rewrite `HakutakuHttp.cs` on top of
  `UnityWebRequest`; nothing else in the SDK touches the network.
- Either copy the `.cs` files into the Unity project, or build the DLL
  (`dotnet build -c Release`) and drop it in `Assets/Plugins`.

## Still open

| Question | Notes |
|---|---|
| **Game servers** | This is a *client* SDK and only knows player tokens. A server SDK needs the server-key path that doesn't exist yet; see [Telemetry ingestion](ingestion.md). |
| **Retries and caching** | None. Calls pass straight through, and a `429` is returned to the caller. The simulator does its own retrying. |
| **Packaging** | Source or DLL by hand for now. A Unity package (UPM) is the likely end state. |

## Related reading

- [Simulator](simulator.md) — the SDK's first user
- [API reference](../reference/api.md) — the routes underneath
- [Auth and sessions](auth.md) — what a player token is
