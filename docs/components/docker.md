# Docker

Everything runs in containers, and the same `Dockerfile` builds locally and in
CI. There are two compose files and they are not interchangeable.

## The image build

`Dockerfile` is a three-stage build:

```dockerfile
# 1. build the Vue admin UI -> static files
FROM node:22 AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2. build and publish the ASP.NET app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY . .
RUN dotnet publish server/Hakutaku.Server.csproj -c Release -o /out

# 3. runtime image: only the published output and the built UI
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out .
COPY --from=web /web/dist ./wwwroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hakutaku.Server.dll"]
```

### Why three stages

The final image contains neither Node nor the .NET SDK — only the ASP.NET
*runtime*, the published DLLs, and the built UI. Stages 1 and 2 are discarded.
That keeps the shipped image small and means a build toolchain is not sitting on
a public-facing host.

Stage 3 is also where **the UI and API become one process**: the Vue build
output lands in `wwwroot`, which ASP.NET serves as static files. That is the
single-origin setup the session cookie depends on — see
[Communication](../architecture/communication.md).

### Why `package*.json` is copied before the rest

```dockerfile
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
```

Docker caches layers. Dependencies only change when `package.json` or
`package-lock.json` change, so copying those first means editing a `.vue` file
does not re-run `npm ci`. Keep that order.

### `.dockerignore`

```text
**/node_modules
**/bin
**/obj
**/dist
**/wwwroot
.git
```

Stage 2 does `COPY . .`, so without this the build context would include
`node_modules` and every build artefact — slow, and it risks a stale local
`wwwroot` overwriting the freshly built UI.

## The two compose files

!!! danger "`compose.dev.yaml` must never be deployed"
    Hardcoded credentials, no TLS, Postgres published to the host. Production is
    `compose.yaml`.

| | `compose.yaml` (production) | `compose.dev.yaml` (local) |
|---|---|---|
| Postgres password | `${POSTGRES_PASSWORD}` from `.env`, required | `hakutaku`, hardcoded |
| `HAKUTAKU_DOMAIN` | Real domain → automatic HTTPS | `:80` → plain HTTP |
| Caddy ports | `80:80` and `443:443` | `80:80` |
| Postgres ports | **none** | `127.0.0.1:5432:5432` |
| App ports | `127.0.0.1:8090:8080` | `127.0.0.1:8090:8080` |
| `HAKUTAKU_ADMIN_PASSWORD` | Forwarded to the app | **Not forwarded** |
| Volumes | `pgdata`, `caddy_data`, `caddy_config` | `pgdata` |

### Services

**`app`** — built from the `Dockerfile`, listens on `8080` inside the container.

```yaml
ports:
  # Caddy on 443 stays the sole public entry point
  # For now, we should only be able to reach this with an SSH tunnel
  # Host port 8090 because Jenkins already holds 8080.
  - "127.0.0.1:8090:8080"
```

The `127.0.0.1:` prefix is doing real work. Without it Docker would publish on
`0.0.0.0` and the app would be on the public internet, bypassing every Caddy
rule. **8090** rather than 8080 because Jenkins already owns 8080 on the VM.

!!! warning "Docker's port rules sit in front of `ufw`"
    Docker publishes ports by writing iptables rules that are evaluated *before*
    the `ufw` chain, so a `ufw deny` does not reliably block a published port.
    Binding to `127.0.0.1` in the compose file is the control that actually
    works here — not the firewall.

**`postgres`** — `postgres:18`, with a healthcheck:

```yaml
healthcheck:
  test: ["CMD-SHELL", "pg_isready -h 127.0.0.1 -U hakutaku -d hakutaku"]
  interval: 3s
  retries: 20
  start_period: 10s
```

`-h 127.0.0.1` forces a **TCP** check rather than a unix-socket one. During
`initdb`, Postgres listens on the socket only, so without that flag the
healthcheck reports healthy while the app still cannot connect.

The app waits for it:

```yaml
depends_on:
  postgres:
    condition: service_healthy
```

`service_healthy`, not just `service_started` — the app runs migrations on boot
and crashes if the database is not accepting connections yet.

In production Postgres has **no `ports:` entry at all**, so it is reachable only
from other containers on the compose network.

**`caddy`** — see [Caddy](caddy.md). The one thing to carry over: it mounts
`./caddy` as a **directory**, not the single file, because a single-file bind
mount pins the inode and git replaces files rather than editing them.

### Volumes

| Volume | Holds | If you delete it |
|---|---|---|
| `pgdata` | The entire database | All data is gone |
| `caddy_data` | Issued certificates **and** the ACME account key | Caddy re-requests certificates — rate-limited to 5/week |
| `caddy_config` | Caddy's autosaved config | Harmless |

!!! danger "`down -v` is the destructive one"
    `docker compose down` stops containers and keeps volumes. `down -v` deletes
    the volumes too — the database *and* the certificates. Safe locally, close
    to never correct in production.

## Everyday commands

```bash
# local dev: database only
docker compose -f compose.dev.yaml up -d postgres

# local: the whole stack (UI at http://localhost:8090 -- not :80)
docker compose -f compose.dev.yaml up --build

# logs, following
docker compose -f compose.dev.yaml logs -f app

# a shell in the running app container
docker compose -f compose.dev.yaml exec app sh

# psql against the dev database
docker compose -f compose.dev.yaml exec postgres psql -U hakutaku -d hakutaku

# stop, keeping data
docker compose -f compose.dev.yaml down

# stop and wipe data
docker compose -f compose.dev.yaml down -v

# does the production image still build?
docker compose -f compose.dev.yaml build
```

## The `-p hakutaku` project name

Every production command includes it:

```bash
docker compose -p hakutaku --env-file /var/lib/jenkins/hakutaku.env -f compose.yaml up -d --build
```

Compose derives the project name from the directory by default, and the project
name is what namespaces containers *and volumes*. Jenkins checks the repo out
into its own workspace directory, so without `-p hakutaku` it would create a
**second stack with a brand-new empty database** rather than reusing `pgdata`
and `caddy_data`.

!!! warning "Never drop `-p hakutaku` from a production command"
    Including read-only ones. `docker compose -f compose.yaml ps` run from the
    wrong directory will cheerfully report nothing running.

## Gotchas

??? failure "The app cannot connect to Postgres after changing credentials"
    Postgres applies `POSTGRES_USER` / `POSTGRES_PASSWORD` **only when its data
    directory is empty**. Changing them later does nothing to an existing
    volume, so the app ends up using new credentials against a database that
    still has the old ones.

    Locally: `docker compose -f compose.dev.yaml down -v`. In production, change
    the password inside Postgres with `ALTER ROLE` instead.

??? failure "I changed a mounted config file and the container ignores it"
    Compose only recreates a container when its *configuration* changes — it
    hashes the service definition into a `com.docker.compose.config-hash` label.
    A change to the *contents* of a mounted file is invisible to that hash.

    For Caddy that is why the pipeline runs `caddy reload` explicitly. For
    anything else, `docker compose up -d --force-recreate <service>`.

??? failure "Port is already allocated"
    Something else holds the host port. The app is on 8090 precisely because
    Jenkins holds 8080 on the VM.

    ```bash
    ss -ltnp | grep 8090      # Linux
    ```

    ```powershell
    netstat -ano | findstr :8090   # Windows
    ```

??? failure "A build succeeds but serves old UI code"
    Check `.dockerignore` still excludes `**/wwwroot` and `**/dist`. A stale
    local build directory getting into the build context can overwrite the fresh
    one from stage 1.

??? failure "The build runs out of memory on the VM"
    The VM has 4 GB and also runs Jenkins and Postgres. Building the UI is the
    heavy step. If this starts happening, the planned fallback is publishing the
    image to a registry and pulling it instead of building on the server — see
    `TODO.md` item 5.
