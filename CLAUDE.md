# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this project is

Hakutaku is the backend for the "Fabled" plugin: a service that mimics **PlayFab** (Microsoft's game-oriented backend) — storing telemetry plus user, player, and character data. The stack is ASP.NET Core (C#) talking to PostgreSQL via EF Core/Npgsql, with a Vue admin UI, all containerized.

**Current state:** `Player`, `Character` (owned by a `Player`) and `TelemetryEvent` (player-scoped, not character-scoped) exist in `server/Models/data.cs`, each with `GET`/`POST` endpoints under `/api/`. There is no separate `User` entity (User and Player are 1:1 for a single-game backend, so `Player` is the account). Admin login/logout/account-management exists (`server/AdminAuth.cs`, backed by the `admin_users`/`admin_sessions` tables). `/api/players`, `/api/characters` and `/api/events` (GET/POST) require a logged-in admin session (`.RequireAdmin()`, as a stopgap until real player-facing auth exists); `/api/admin/admins` is further restricted to the owner. **The only public game-data endpoints are the three player-auth routes in `server/PlayerAuth.cs`**: `POST /api/players/register` (find-or-create by device ID through a `player_identities` row, returns a Bearer session token), `POST /api/players/link/email` (needs that token; adds an email + password identity) and `POST /api/players/login` (email + password). `register` and `login` are rate-limited; a player token currently unlocks nothing but `link/email`. `Player` no longer has a `DeviceId` column — it is a `device` row in `player_identities`, and a device ID works like a password. No email verification or password reset exists yet. Caddy (`caddy/Caddyfile`) is now default-deny: only `/Health` and the three player-auth routes are reachable from the public internet; everything else, including the whole admin UI, is tunnel-only. A production stack is live on the DigiPen team43 VM at `https://51.79.242.169.nip.io`, and Jenkins redeploys it on every merge to `master` (see `Jenkinsfile`). See [TODO.md](TODO.md) for what's done and what's left. Note: a Vue admin frontend (`web/src/pages/`) and an MkDocs wiki (`docs/`, public at `docs.51.79.242.169.nip.io`) exist now too — this file hasn't been brought fully up to date on those yet.

One workstream has not started: a planned C++ telemetry ingestion service that will call the `/api/events` endpoint (or write directly to Postgres — undecided).

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

### Production deploy (team43 VM)
`compose.yaml` is the production file (Caddy on 80/443 with automatic HTTPS, Postgres not port-mapped). It needs a `.env` next to it with `HAKUTAKU_DOMAIN` and `POSTGRES_PASSWORD` — `.env` is gitignored, so it has to be created on the VM by hand (see `.env.example`). The domain is `51.79.242.169.nip.io`, a free wildcard-DNS name that resolves to the VM's IP.
```bash
# on the VM, in ~/Hakutaku
git pull
docker compose -f compose.yaml up -d --build
docker compose -f compose.yaml logs -f caddy
```
Avoid repeated `docker compose down -v` against the real domain: it wipes the `caddy_data` volume and forces a fresh Let's Encrypt certificate request, which is rate-limited to 5 duplicates per week.

## Architecture

- **`server/Program.cs`** is a minimal-API file — the player, character and event endpoints are mapped here directly (no controllers yet), and it wires up the middleware. The admin endpoints live in `server/AdminAuth.cs` and are mapped with `app.MapAdminEndpoints()`; the public player-auth endpoints live in `server/PlayerAuth.cs` (`app.MapPlayerEndpoints()`), which reuses `AdminAuth`'s Argon2id and token-hash helpers. Read `Program.cs` top to bottom to see the whole request pipeline.
- **Admin auth**: passwords are Argon2id (encoded string in `pw_hash`), session tokens are random and only their SHA-256 is stored. Login is by `Username` (unique, case-insensitive, matched on `lower(username)`), not email — `Email` on `AdminUser` is optional, unused account metadata for future report hooks. The owner account (username `admin`) is created at startup when no admins exist, with its password from `HAKUTAKU_ADMIN_PASSWORD` (or generated and logged once). The app trusts `X-Forwarded-For`/`X-Forwarded-Proto` because only Caddy can reach it in production; the per-IP login rate limit, the session IP, and whether the session cookie gets marked `Secure` all depend on that. Login sets an `HttpOnly`/`SameSite=Strict` cookie (`hakutaku_admin_session`) rather than returning a token in the body; `GET /api/admin/me` is how a client checks whether it's still logged in. A session has two independent expiries in `AdminSession`, whichever comes first: `IdleExpiresAt` (pushed forward by `FindActiveSession` on every authenticated call, capped at `ExpiresAt`) and the fixed `ExpiresAt` (absolute lifetime, never moves). `RequireOwner` (also in `AdminAuth.cs`) gates `POST /api/admin/admins` (create) and `DELETE /api/admin/admins/{id}` (deactivate, one-way — sets `DisabledAt`, no restore) to sessions whose `Role` is `Owner`; there's only ever one owner (seeded at first start, can't be created via the endpoint), and it can never be deactivated through that endpoint regardless of caller. Deactivating an admin also revokes their active sessions immediately via `ExecuteUpdateAsync`, rather than waiting for natural expiry. `RequireAdmin<TBuilder>` is the looser version (owner **or** admin) — a chainable extension method used like `.RequireRateLimiting(...)`, e.g. `app.MapGet(...).RequireAdmin()`, applied to the `/api/players`/`/api/characters`/`/api/events` endpoints in `Program.cs`.
- **EF Core wiring**: `server.Models.Db` (in `server/Models/data.cs`) is the single `DbContext`, registered in `Program.cs` via `AddDbContext` reading `ConnectionStrings:Db`. Entities are added as `DbSet<T>` properties on `Db`.
- **Static file / SPA fallback ordering matters**: `UseDefaultFiles()` / `UseStaticFiles()` / `MapFallbackToFile("index.html")` come *after* the API route mappings. Static-file middleware skips any request that already matched an endpoint, so **do not map `/` to an endpoint** — it would shadow the Vue UI fallback (this is called out in a comment in `Program.cs`).
- **Two different connection strings, two different readers**: `server/appsettings.Development.json` has a hardcoded dev connection string that `dotnet watch` reads directly on the host. Compose instead injects `ConnectionStrings__Db` as an env var (see `compose.dev.yaml`), which overrides it inside the container. `.env` (copy from `.env.example`) is read by Compose only, never by `dotnet watch`.
- **Docker build is multi-stage** (`Dockerfile`): stage 1 builds the Vue UI (`web/` -> `dist`), stage 2 publishes the ASP.NET app, stage 3 copies the published API plus the built UI into `wwwroot` of the runtime image — this is how one container serves both API and UI.
- **Caddy** (`caddy/Caddyfile`, mounted as a directory so edits are visible without recreating the container) is a reverse proxy in front of the API, and the only public entry point. It is **default-deny**: only `/Health` is proxied (the Jenkins smoke test needs it) and every other path gets a `404`, so the admin UI and API are reachable solely through the SSH tunnel documented in the readme — add a `handle` block per route when player/server endpoints need public access. The site address is `{$HAKUTAKU_DOMAIN}` (Caddy's own env syntax), and both compose files feed it: `compose.yaml` sets a real domain, so Caddy gets a Let's Encrypt certificate and enables HTTPS automatically; `compose.dev.yaml` sets `:80`, which means plain HTTP with no certificate. A bare `:80` site address can never get HTTPS because Let's Encrypt only issues certificates for domain names.
- **`sdk/` and `simulator/`** are currently empty placeholder directories (git doesn't track them until they have content).
