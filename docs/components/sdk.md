# SDK

!!! info "Stub — no code exists yet"
    `sdk/` contains only a `.gitkeep`. This page records the intent and the
    constraints a future client library has to respect.

## What it is meant to be

A client library for talking to Hakutaku, so a game — the **Fabled** plugin —
does not hand-roll HTTP calls and JSON parsing against this API.

Nothing has been decided about it: not the language, not the surface, not the
packaging. `TODO.md` item 8 records only that the directory is a placeholder.

## Open questions

| Question | Notes |
|---|---|
| **Which language?** | The plugin's language is the obvious driver. C++ would share ground with the [simulator](simulator.md). |
| **Who is it for?** | A game client, a game *server*, or both? They need different credentials and have very different trust levels. |
| **Sync or async?** | Game clients cannot block a frame on a network call. |
| **How is it packaged?** | NuGet, vcpkg, or vendored source. |
| **Does it cache?** | Local state plus retry, or straight pass-through. |

## Constraints it will have to respect

These are not open questions — they follow from how the backend works.

### Treat IDs as opaque strings

Player, character and event IDs are GUIDs today. Planning documents target
`bigint` instead.

So **never parse, format, pad or assume the length of an ID.** Store and pass
them as strings. Get this wrong and the eventual key migration becomes a hunt
through every client rather than a server-side change.

### Admin session cookies are not for programs

The session cookie is `HttpOnly`, `SameSite=Strict`, minted by a human typing a
password, and expires after 12 hours — with a 30-minute idle timeout on top. It
also carries admin privileges across the entire API.

A client library must not use it. Non-admin credentials
(player auth, server keys) do not exist yet, which means **the SDK is blocked
on the same work as the simulator**.

### The API surface is small and will move

Today: `GET`/`POST` on players, characters and events, and that is all. No
`PUT`, no `DELETE`, no paging, no filtering — list endpoints return the entire
table.

Two changes are already intended:

- Paths become **singular**: `/api/players` → `/api/player`.
- The game-data endpoints become **publicly reachable** with their own auth,
  rather than admin-gated.

Build against the [API reference](../reference/api.md), and expect it to shift.

### Errors are not uniform

- `400`, `401`, `403`, `404` and `409` return `{ "error": "…" }`.
- `429` from the login rate limiter has an **empty body**.
- A bad `playerId` currently returns a bare `500` — a foreign-key violation that
  is not caught and converted.
- `204` responses (login, logout, delete) have no body at all, so parsing one as
  JSON throws.

Any client needs to handle all four shapes. `web/src/api.ts` is a working
reference for doing it in one place — see [Web UI](web.md#apits-the-single-chokepoint).

## Related reading

- [API reference](../reference/api.md)
- [Simulator](simulator.md) — shares the server-key blocker
- [Practices](../architecture/practices.md#treat-ids-as-opaque)
