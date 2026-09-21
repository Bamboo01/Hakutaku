# TODO

Roughly in dependency order, across three workstreams: the backend build
(1-3), production/CI-CD (4-6), and the C++ ingestion plane (7). See
[CLAUDE.md](CLAUDE.md) for the overall project context.

---

## 1. Expand the domain model

**Done.** `Player`, `Character` and `TelemetryEvent` exist in
`server/Models/data.cs` with a migration and `GET`/`POST` endpoints for each.

- [x] `User` / title-account entity — decided not needed: this backend serves a
      single game, so User and Player are 1:1 and `Player` is the account
- [x] `Character` entity, owned by a `Player`
- [x] A telemetry event entity + ingestion endpoint (`POST /api/events`) — this
      is also the contract the C++ ingestion plane (item 7) will eventually
      call into. Telemetry is player-scoped, not character-scoped.
- [x] EF Core migrations for each
- [x] CRUD/query endpoints for each, following the pattern already in
      `server/Program.cs`

---

## 2. Auth / first-run admin

`.env.example` used to document `HAKUTAKU_ADMIN_USER` and
`HAKUTAKU_ADMIN_PASSWORD`. Nothing reads them, because none of it exists yet:

- [ ] No user entity — the only table is `Players` (see item 1 — the `User`
      entity work and auth work overlap, decide together whether login lives on
      that entity or a separate one)
- [ ] No password hashing, no login endpoint, no sessions or tokens
- [ ] No `[Authorize]` anywhere; `/api/players` is fully open to the internet

This blocks deploying, not just cosmetically — right now anyone who finds the
URL can read and write the player table. Decide explicitly whether auth is in
scope for the MVP or deferred past it.

---

## 3. MVP verification pass

- [x] Every endpoint from item 1 hit end to end with curl: player → character →
      event, read back, foreign keys and the server-side timestamp confirmed
- [ ] A saved Postman collection for the same flow, so it can be re-run
- [ ] Confirm `docker compose -f compose.dev.yaml up --build` still builds and
      serves the full stack. `compose.dev.yaml` now sets `HAKUTAKU_DOMAIN: ":80"`
      for Caddy because the Caddyfile is shared with `compose.yaml` — untested.

---

## 4. Production deploy: a real `compose.yaml` + TLS

**Done.** `compose.yaml` + the updated `Caddyfile` are deployed and verified on
the team43 VM (`51.79.242.169`), using `51.79.242.169.nip.io` as
`HAKUTAKU_DOMAIN` (a free wildcard-DNS service that resolves straight to the
IP, so no domain registration was needed). Caddy obtained a real Let's Encrypt
certificate on the first attempt and `/Health` and `/api/players` both respond
correctly over HTTPS through the full path (browser → Caddy → app → postgres).

**Watch out for next time:** the cert was requested from Let's Encrypt's
*production* CA directly (no staging test first). That's fine as a one-off,
but Let's Encrypt allows only 5 duplicate certificates per week for the same
domain — so avoid repeated `docker compose down -v` (which wipes the
`caddy_data` volume, forcing a fresh certificate request) in quick succession
against this same domain while testing.

Original notes below, kept for reference.

Today there is exactly one compose file, `compose.dev.yaml`, and it is
local-only: hardcoded credentials, no TLS, Postgres bound to your machine. A
production deploy needs a second file. **Do not deploy `compose.dev.yaml`.**

### What Caddy is actually doing

Caddy is a **reverse proxy**. It's the only thing listening on the public
internet. A browser talks to Caddy; Caddy forwards the request to the API
container on its internal port and passes the response back.

```
browser ──https:443──> caddy ──http:8080──> app ──5432──> postgres
         (public)              (docker network, not public)
```

Two reasons to bother: the API never gets exposed directly, and Caddy handles
HTTPS certificates for you so the app doesn't have to know TLS exists.

### Why there's no HTTPS yet

`Caddyfile` currently reads (Caddy accepts the brace on its own line, though it
warns — see item 6):

```
:80
{
	reverse_proxy app:8080
}
```

`:80` means "serve plain HTTP on port 80, for any hostname." Caddy will happily
get you a certificate automatically, but **only if you give it a real domain
name instead of a bare port.** It can't get a certificate for `localhost` or for
an IP address, because of how the issuing process works:

Caddy asks Let's Encrypt for a certificate for `backend.example.com`. Let's
Encrypt has to verify you actually control that name, so it makes an HTTP
request to `http://backend.example.com/.well-known/acme-challenge/<random>` and
checks that your server answers with the expected value. That round trip is why:

- the domain's **DNS A record must already point at the machine** before you
  start Caddy, and
- **port 80 must stay open** even though you want HTTPS. It's used for the
  challenge and for redirecting http → https.

### What needs to change

**`Caddyfile`** — swap the port for a domain:

```
{$HAKUTAKU_DOMAIN} {
	reverse_proxy app:8080
}
```

`{$HAKUTAKU_DOMAIN}` is *Caddy's* environment-variable syntax. Note the brace
and dollar are the other way round from Compose's `${HAKUTAKU_DOMAIN}` — easy to
mix up, and Caddy will treat a wrong one as a literal hostname.

There's no redirect directive to write: once the site address is a real domain,
Caddy turns on automatic HTTPS, which issues the certificate *and* installs the
http→https redirect on port 80 for you. That's the whole reason the snippet
looks too short to be doing this much.

**`compose.yaml`** (new file) — the caddy service needs three things it doesn't
have today:

```yaml
  caddy:
    image: caddy:2
    restart: unless-stopped
    ports:
      - "80:80"      # required for the ACME challenge + http→https redirect
      - "443:443"    # missing today
    environment:
      HAKUTAKU_DOMAIN: ${HAKUTAKU_DOMAIN}
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data       # certificates + ACME account key
      - caddy_config:/config
    depends_on:
      - app

volumes:
  caddy_data:
  caddy_config:
```

**The `caddy_data` volume is the one people forget.** Caddy stores your issued
certificates *and* your Let's Encrypt account key in `/data`. Without a volume,
that's wiped every time the container is recreated, so Caddy re-requests
certificates from scratch on each deploy. Let's Encrypt allows **5 duplicate
certificates per week** — burn through that and your site has no valid
certificate until the window resets. Nothing warns you until it's too late.

### Test with staging first

While you're getting this working, point Caddy at Let's Encrypt's staging
environment, which has far looser rate limits. Add this as a global block at the
very top of the `Caddyfile`:

```
{
	acme_ca https://acme-staging-v02.api.letsencrypt.org/directory
}
```

Staging certificates are *not* trusted by browsers, so you'll get a warning page
— that's expected. It proves DNS, ports, and the challenge all work. Once the
warning is the only problem left, delete the block, run
`docker compose down && docker compose up -d`, and you'll get a real
certificate.

Watch it happen with `docker compose logs -f caddy`.

### Deploy checklist, once the above exists

- [x] Host with a public IP, ports 22/80/443 open, nothing else
- [x] DNS pointing at it — used `51.79.242.169.nip.io` instead of a registered
      domain, no A record to manage
- [x] Docker installed (`curl -fsSL https://get.docker.com | sh`)
- [x] 2 GB swap (pre-set on the team43 VM)
- [x] `.env` created with a real `POSTGRES_PASSWORD` and `HAKUTAKU_DOMAIN`
      (`openssl rand -base64 32` for secrets) — local-only, not committed
- [x] Postgres **not** port-mapped in `compose.yaml` — internal network only

---

## 5. Publish an image

**Decided: build on the server, no registry.** Jenkins checks the repo out on the
VM and runs `docker compose up -d --build` from its workspace (see item 6). The
build takes about a minute on the VM. Publishing to GHCR only becomes worth it if
the VM's 4 GB of RAM turns out to be too tight for building alongside Jenkins.

The readme's old `docker compose pull && docker compose up -d` update
instructions are still wrong, because no image is published anywhere.

---

## 6. CI/CD with Jenkins

**Mostly done.** Jenkins runs on the team43 VM, installed from the official apt
repo. Its web UI is not public: reach it with an SSH tunnel
(`ssh -L 8081:localhost:8080 team43@51.79.242.169`, then `http://localhost:8081`).
The `Jenkinsfile` in the repo root deploys `compose.yaml` and then smoke-tests
`/Health` over HTTPS. The job polls GitHub every ~2 minutes and watches `master`.

- [x] Stand up Jenkins on the VM
- [x] Pipeline stages: deploy → smoke test. There is no separate test stage
      because there is no test suite yet.
- [x] Deploy mechanism decided (item 5): build on the server
- [x] Encode it in a `Jenkinsfile`
- [ ] Confirm a merge to `master` triggers a build by itself. Build #2 (on master)
      passed, but it was started by hand with Build Now.

Things that would trip up a rebuild of this setup: the `jenkins` user must be in
the `docker` group; the pipeline reads secrets from
`/var/lib/jenkins/hakutaku.env` (a copy of the VM's `.env`, owned by `jenkins`,
mode 600) because it can't read `/home/team43`; and `-p hakutaku` in the
`Jenkinsfile` must stay, or a second stack with a fresh empty database gets
created.

---

## 7. C++ telemetry ingestion plane (stretch)

Personal addition, not part of the core backend scope. Depends on item 1's
telemetry event endpoint existing and its contract being stable first.

- [ ] Decide the contract: does it POST into the ASP.NET ingestion endpoint
      (keeps one source of truth for schema/validation), or write straight to
      Postgres (faster, duplicates validation logic)?
- [ ] Scope it as a separate service/binary — it should not block items 1-6

---

## 8. `sdk/` and `simulator/`

Both exist as empty directories. Git doesn't track empty directories, so they
will silently vanish if never populated. Add a `.gitkeep` to each if they're
placeholders, or delete them.

---

## 9. Smaller items

- [ ] `server/appsettings.Development.json` has the dev password in source
      control. Fine for local-only credentials; move to user-secrets if it ever
      becomes a real one.
- [ ] `server/Models/data.cs` still says "This is just sample code" — the
      domain model is real now, so the comment can go.
- [x] Caddyfile formatting warning — fixed when the Caddyfile was rewritten for
      `{$HAKUTAKU_DOMAIN}`; the VM's Caddy logs no longer show it.
