# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this project is

Hakutaku is the backend for the "Fabled" plugin: a service that mimics **PlayFab** (Microsoft's game-oriented backend) — storing telemetry plus user, player, and character data. The stack is ASP.NET Core (C#) talking to PostgreSQL via EF Core/Npgsql, with a Vue admin UI, all containerized.

**Current state is an early scaffold**, not the target shape: only a `Player` entity and `/api/players` exist so far (see `server/Models/data.cs`). There is no auth, no `User`/`Character`/telemetry-event entities yet, and no production deploy — see [TODO.md](TODO.md) for the known gaps. Don't assume the intended PlayFab-like domain model exists in code yet; check what's actually there.

Two additional workstreams exist outside this repo's current code: a Jenkins CI/CD pipeline (build → image → deploy to a VM), and a planned C++ telemetry ingestion service that will call into whatever ingestion endpoint this API exposes (or write directly to Postgres — undecided). Neither has started.

## Commands

### Local dev loop (hot reload, run from repo root)
```bash
docker compose -f compose.dev.yaml up -d postgres   # Postgres only, in a container
cd server && dotnet watch                            # API on :5008
cd web && npm install && npm run dev                  # UI on :5173, proxies /api -> :5008 (see web/vite.config.ts)
```
Open http://localhost:5173.

### Full stack in Docker (Caddy :80 -> API :8080 -> Postgres)
```bash
docker compose -f compose.dev.yaml up --build
```
`compose.dev.yaml` is **local-only** (hardcoded `hakutaku`/`hakutaku` credentials, no TLS) — never deploy it. Postgres only applies its user/password on first init; if you change them, reset with `docker compose -f compose.dev.yaml down -v` (destroys local data).

### EF Core migrations
Migrations run automatically at startup (`Database.Migrate()` in `server/Program.cs`) — you do not need to run `database update` manually in dev.
```bash
dotnet tool install --global dotnet-ef   # once
cd server
dotnet ef migrations add <Name>
```

### Build/test
```bash
cd server && dotnet build                # API
cd web && npm run build                  # vue-tsc typecheck + vite build -> web/dist, copied into wwwroot by Dockerfile
```
No test suite exists in either `server/` or `web/` yet.

### Verify the production Docker image still builds
```bash
docker compose -f compose.dev.yaml build
```

## Architecture

- **`server/Program.cs`** is a single minimal-API file — all endpoints are mapped here directly (no controllers yet). Read it top to bottom to see the whole request pipeline.
- **EF Core wiring**: `server.Models.Db` (in `server/Models/data.cs`) is the single `DbContext`, registered in `Program.cs` via `AddDbContext` reading `ConnectionStrings:Db`. Entities are added as `DbSet<T>` properties on `Db`.
- **Static file / SPA fallback ordering matters**: `UseDefaultFiles()` / `UseStaticFiles()` / `MapFallbackToFile("index.html")` come *after* the API route mappings. Static-file middleware skips any request that already matched an endpoint, so **do not map `/` to an endpoint** — it would shadow the Vue UI fallback (this is called out in a comment in `Program.cs`).
- **Two different connection strings, two different readers**: `server/appsettings.Development.json` has a hardcoded dev connection string that `dotnet watch` reads directly on the host. Compose instead injects `ConnectionStrings__Db` as an env var (see `compose.dev.yaml`), which overrides it inside the container. `.env` (copy from `.env.example`) is read by Compose only, never by `dotnet watch`.
- **Docker build is multi-stage** (`Dockerfile`): stage 1 builds the Vue UI (`web/` -> `dist`), stage 2 publishes the ASP.NET app, stage 3 copies the published API plus the built UI into `wwwroot` of the runtime image — this is how one container serves both API and UI.
- **Caddy** (`Caddyfile`) is a reverse proxy in front of the API; in dev it just serves plain HTTP on `:80`. It has no real domain configured yet, so no automatic HTTPS — see TODO.md item 1 for what a production Caddyfile/compose needs.
- **`sdk/` and `simulator/`** are currently empty placeholder directories (git doesn't track them until they have content).
