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

HakutakuClientAPI.LoginWithDeviceToken(
    new LoginWithDeviceTokenRequest { DeviceToken = storedDeviceToken },
    result => Debug.Log("Logged in as " + result.PlayerId),
    error => Debug.LogError(error.GenerateErrorReport()));
```

**An instance per player**, for anything that holds several players at once,
such as the simulator or a dedicated server. Every call also has an `…Async`
form that returns a result instead of taking callbacks:

```csharp
var client = new HakutakuClientInstanceAPI(new HakutakuApiSettings { ServerUrl = "http://localhost:5008" });

var login = await client.LoginWithDeviceTokenAsync(new LoginWithDeviceTokenRequest { DeviceToken = storedDeviceToken });
if (login.Error != null)
    Console.WriteLine(login.Error.GenerateErrorReport());
else
    Console.WriteLine(login.Result.PlayerId);
```

No call throws on an HTTP or network failure. Every result has exactly one of
`Result` and `Error` set.

## The launch flow

The SDK doesn't store anything. The game keeps two things between launches: the
**device token**, while the player is a guest, and the **session**, valid 30
days. The session is **all three** of `PlayerId`, `SessionTicket` and
`SessionExpiration` from the authentication context. On each launch:

1. **A saved session that hasn't expired?** Put all three back into
   `HakutakuSettings.staticPlayer`, and you're signed in with no network call.
   Restoring the ticket without the `PlayerId` looks fine at first:
   `IsClientLoggedIn()` only checks the ticket. But calls that send the player's
   id, such as `WritePlayerEvent`, would then send none.
2. **Otherwise, a saved device token?** Call `LoginWithDeviceToken`. If it fails
   with `NotAuthenticated`, the token is gone: go to step 3. **Don't register a
   new guest automatically.** That silently replaces the player's account with an
   empty one, the classic "I reinstalled and lost everything".
3. **Otherwise, show a title screen** with **Start**, which calls
   `RegisterGuest` and saves `LoginResult.DeviceToken`, and **I have an
   account**, which calls `LoginWithEmailAddress`.

And two moments to handle:

- **`VerifyEmail` returns `DeviceTokenRemoved = true`:** delete the stored
  device token. The server has already deleted it, so from now on the player
  signs in by email. The current session keeps working.
- **The player signs into a different account by email:** delete the stored
  device token too. Otherwise the next launch signs back into the throwaway guest.

!!! warning "Until refresh tokens exist, a verified player retypes their password every 30 days"
    A verified player has no device token, so between launches only the saved
    session keeps them signed in. When it expires, they sign in by email again.
    Refresh tokens (`TODO.md`) will remove this.

!!! danger "The hardware ID is not a credential"
    `RegisterGuest` needs a `HardwareId`, and `SystemInfo.deviceUniqueIdentifier`
    is the right thing to send. The server only **records** it, for support and
    spotting reroll farms, because it isn't secret: analytics, ad and crash SDKs
    read and send it. The credential is the **device token** the server mints.
    Store that like a password: `PlayerPrefs` is fine for a prototype, and a
    shipped game should use the iOS Keychain or the Android Keystore.

## The calls

| SDK call | PlayFab counterpart | Route | Works with a player token today? |
|---|---|---|---|
| `RegisterGuest` | `LoginWithCustomID` with `CreateAccount` | `POST /api/players/register` | Yes (public) |
| `LoginWithDeviceToken` | `LoginWithCustomID` | `POST /api/players/login/device` | Yes (public) |
| `LoginWithEmailAddress` | same | `POST /api/players/login/email` | Yes (public) |
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
- **Creating and signing in are separate calls.** PlayFab's `LoginWithCustomID`
  takes a client-chosen ID and a `CreateAccount` flag. Here the server mints
  the credential in `RegisterGuest`, and `LoginWithDeviceToken` never creates.
- **Display names** are optional on `RegisterGuest`, and
  `UpdateUserTitleDisplayName` changes them any time. They're 3–25 characters and
  **not unique**, so never use one to identify a player.
- **`ForgetAllCredentials` is client-side only.** There is no player logout
  route, so the session stays valid on the server until it expires (30 days) or a
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
