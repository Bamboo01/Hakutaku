# Hakutaku

Welcome. Hakutaku is the backend for the **Fabled** plugin — a self-hosted
service that does roughly what Microsoft's **PlayFab** does: store telemetry
plus the user, player and character data a game needs, and hand it back over
HTTP.

If you have just been handed this repo, read [Setup](setup.md) first, get it
running, then come back here and pick a page.

!!! tip "This wiki is hosted"
    Live at **<https://docs.51.79.242.169.nip.io>**, served from `master` — no
    tunnel and no local setup needed to read it. To run it locally with live
    reload, see [Mode C](setup.md#mode-c-docs).

## What it actually is right now

Three things in Docker containers:

| Piece | What it is | Where it lives |
|---|---|---|
| **Server** | ASP.NET Core 10 minimal API | `server/` |
| **Web UI** | Vue 3 admin panel, served by the server | `web/` |
| **Database** | PostgreSQL 18 via EF Core | migrations in `server/Migrations/` |
| **Caddy** | Reverse proxy, the only public door | `caddy/Caddyfile` |

It is an **early scaffold**, and the docs say so wherever it matters. Three
entities exist (`Player`, `Character`, `TelemetryEvent`), admin login works,
and a production stack runs on the DigiPen team43 VM with Jenkins redeploying
on every merge to `master`. Players can register and log in, but a player
token doesn't open game data yet. There is no test suite; the closest thing is
the simulator, whose mock players drive the API through the client SDK.

!!! note "The honest status line"
    Everything under [Components](components/server.md) describes code that
    exists. Anything planned-but-absent is called out as such, inline. If a
    page and the code disagree, the code wins — please fix the page.

## Site map

<div class="grid cards" markdown>

-   **[Setup](setup.md)**

    Install Docker, Node and the .NET SDK from nothing, then run the server,
    the web UI and these docs. Written assuming no prior context.

-   **Architecture**

    [Project structure](architecture/structure.md) — the repo layout, the tech
    stack and why each piece was picked.
    [How the pieces talk](architecture/communication.md) — the request
    lifecycle end to end.
    [Practices and exposure](architecture/practices.md) — what is public, what
    is not, and the conventions we hold to.

-   **Components**

    [Caddy](components/caddy.md) · [Docker](components/docker.md) ·
    [Database](components/database.md) · [Server](components/server.md) ·
    [Auth and sessions](components/auth.md) · [Web UI](components/web.md) ·
    [Simulator](components/simulator.md) · [SDK](components/sdk.md) ·
    [Telemetry ingestion](components/ingestion.md)

-   **[Operations](operations/deploy.md)**

    The VM, the Jenkins pipeline, the SSH tunnel, and what to do when a deploy
    looks like it did nothing.

-   **Reference**

    [API](reference/api.md) — every endpoint, request body and error shape.
    [Contributing](reference/contributing.md) — branches, migrations, line
    endings, house style.

</div>

## Where to start, by job

| You are… | Read, in order |
|---|---|
| Getting it running locally | [Setup](setup.md) |
| Adding an API endpoint | [Server](components/server.md) → [Database](components/database.md) → [API](reference/api.md) |
| Adding a page to the admin UI | [Web UI](components/web.md) → [Auth and sessions](components/auth.md) |
| Touching anything security-shaped | [Auth and sessions](components/auth.md) → [Practices](architecture/practices.md) |
| Debugging prod | [Operations](operations/deploy.md) → [Caddy](components/caddy.md) |
| Wondering why a change didn't deploy | [Operations](operations/deploy.md#when-a-deploy-looks-like-it-did-nothing) |

## Other docs in the repo

These are **not** duplicated here, on purpose:

- **[`TODO.md`](https://github.com/Bamboo01/Hakutaku/blob/master/TODO.md)** —
  the roadmap. What's done, what's next, and the reasoning behind decisions
  already made.
- **`CLAUDE.md`** — a dense architecture brief written for Claude Code, not for
  humans. Accurate, but deliberately terse.
- **`readme.md`** — a short front door that points back here.
