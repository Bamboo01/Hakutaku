# Hakutaku

Backend for the **Fabled** plugin: a self-hosted service that does what PlayFab
does — telemetry plus user, player and character data, over HTTP.

ASP.NET Core 10 + PostgreSQL 18 + a Vue 3 admin UI, all in Docker, behind Caddy.

**Status: early scaffold.** Three entities (`Player`, `Character`,
`TelemetryEvent`) with `GET`/`POST` endpoints, admin login, and a production
stack on the DigiPen team43 VM that Jenkins redeploys on every merge to
`master`. No player-facing auth yet, and no test suite.

## Documentation

**The full wiki lives in [`docs/`](docs/).** Serve it with:

```bash
pip install -r requirements-docs.txt
mkdocs serve        # http://localhost:5020
```

Or without installing Python:

```bash
docker run --rm -p 5020:5020 -v "${PWD}:/docs"   squidfunk/mkdocs-material serve --dev-addr 0.0.0.0:5020
```

Start at [`docs/index.md`](docs/index.md), or go straight to:

| | |
|---|---|
| [Setup](docs/setup.md) | Install everything from scratch and run it |
| [Project structure](docs/architecture/structure.md) | Layout, tech stack, why |
| [API reference](docs/reference/api.md) | Every endpoint and error shape |
| [Auth and sessions](docs/components/auth.md) | Hashing, cookies, expiry, roles |
| [Deploy and CI/CD](docs/operations/deploy.md) | The VM, Jenkins, the SSH tunnel |
| [Contributing](docs/reference/contributing.md) | Branches, migrations, house style |

[TODO.md](TODO.md) is the roadmap.

## Quick start

Requires Docker. For development, also the .NET 10 SDK and Node 22+.

```bash
git clone https://github.com/Bamboo01/Hakutaku.git
cd Hakutaku
```

**Everything in Docker:**

```bash
docker compose -f compose.dev.yaml up --build
```

Open <http://localhost:8090>. Not port 80 — Caddy is default-deny and serves
only `/Health`. See [Caddy](docs/components/caddy.md).

**Dev loop, with hot reload:**

```bash
docker compose -f compose.dev.yaml up -d postgres   # terminal 1
cd server && dotnet watch                           # terminal 2, API on :5008
cd web && npm install && npm run dev                # terminal 3, UI on :5173
```

Open <http://localhost:5173>.

The first start seeds an owner account with username `admin`. Set
`HAKUTAKU_ADMIN_PASSWORD` beforehand, or read the generated one out of the log —
it is printed exactly once. Details in
[Setup](docs/setup.md#your-first-login).

`compose.dev.yaml` is **local-only** (hardcoded credentials, no TLS) — never
deploy it. Production is `compose.yaml`; see
[Deploy](docs/operations/deploy.md).
