# Hakutaku

ASP.NET Core API + Vue admin UI + Postgres, all in Docker.

**Status: early scaffold.** One entity (`Player`), three endpoints (`/Health`,
`GET`/`POST /api/players`), no auth. The dev loop and the Docker build work;
there is no production deploy yet. See [TODO.md](TODO.md) for what's missing.

## Requirements

- Docker (Docker Desktop on Windows/macOS)
- For development: the .NET 10 SDK and Node 22 or newer

## Quick start

```bash
git clone https://github.com/Bamboo01/Hakutaku.git
cd Hakutaku
docker compose -f compose.dev.yaml up --build
```

Open <http://localhost>.

Everything runs in containers: Caddy on :80 proxies to the API on :8080, which
serves the built Vue UI out of `wwwroot`. Database credentials are hardcoded to
`hakutaku`/`hakutaku` and there is no TLS, so keep this on your own machine.

Compose waits for Postgres to be healthy before starting the API, but Caddy
comes up immediately — so for a second or two after `up` you may get a **502**
while the API finishes booting. Refresh; it clears on its own.

## Developing

Run only Postgres in a container so the app and UI hot-reload:

```bash
docker compose -f compose.dev.yaml up -d postgres

cd server && dotnet watch               # terminal 1, API on :5008
cd web && npm install && npm run dev    # terminal 2, UI on :5173
```

Open <http://localhost:5173>. Vite proxies `/api` to the API, so CORS never
comes up.

> **Already ran an older build of this repo?** The Postgres volume was created
> with different credentials, and the API will fail to connect on startup.
> Postgres only applies `POSTGRES_USER`/`POSTGRES_PASSWORD` when its data
> directory is empty — changing them later does nothing. Reset with
> `docker compose -f compose.dev.yaml down -v`, which deletes all local data.

### Migrations

Migrations run automatically at startup — `Database.Migrate()` in
`server/Program.cs:24`. To add one:

```bash
dotnet tool install --global dotnet-ef   # once
cd server
dotnet ef migrations add SomeChange
```

### Checking the production image still builds

```bash
docker compose -f compose.dev.yaml build
```

## Configuration

The defaults work with no configuration. To change one, copy `.env.example` to
`.env` next to `compose.dev.yaml` and uncomment the line you want; Compose picks
it up automatically. (`.env` is read by Compose only — `dotnet watch` on the host
reads `server/appsettings.Development.json`.)

| Variable | Purpose |
|---|---|
| `DB_CONNECTION` | Full connection string the API uses. Defaults to the bundled Postgres; set it to point at an external database. |

That is currently the *only* variable any code reads. Domain and admin-bootstrap
settings are not implemented yet — see [TODO.md](TODO.md).

## Backups

The dev database is named `hakutaku` and owned by `hakutaku`:

```bash
# back up
docker compose -f compose.dev.yaml exec -T postgres \
  pg_dump -U hakutaku hakutaku | gzip > backup-$(date +%F).sql.gz

# restore
gunzip -c backup-2026-09-16.sql.gz | \
  docker compose -f compose.dev.yaml exec -T postgres psql -U hakutaku hakutaku
```
