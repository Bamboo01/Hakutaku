# TODO

Roughly in dependency order, across three workstreams: the backend build
(1-3), production/CI-CD (4-6), and the C++ ingestion plane (7). See
[CLAUDE.md](CLAUDE.md) for the overall project context.

---

## 1. Expand the domain model

Today the only entity is `Player` (`server/Models/data.cs`), with `/api/players`
as the only real endpoint. To actually mimic PlayFab, this needs to grow into:

- [ ] `User` / title-account entity — an account distinct from a `Player`
- [ ] `Character` entity, owned by a `Player`
- [ ] A telemetry event entity + ingestion endpoint (e.g. `POST /api/events`) —
      this is also the contract the C++ ingestion plane (item 7) will eventually
      call into
- [ ] EF Core migrations for each (`dotnet ef migrations add <Name>` from `server/`)
- [ ] CRUD/query endpoints for each, following the pattern already in
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

- [ ] Postman collection hitting every endpoint from item 1 — confirm rows land
      in Postgres (`docker compose -f compose.dev.yaml exec postgres psql -U
      hakutaku hakutaku`)
- [ ] Confirm `docker compose -f compose.dev.yaml up --build` still builds and
      serves the full stack with the new entities (this already works for
      `Player` per the readme — don't break it as the model grows)

---

## 4. Production deploy: a real `compose.yaml` + TLS

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

- [ ] Host with a public IP, ports 22/80/443 open, nothing else
- [ ] DNS A record pointing at it — confirm with `ping backend.example.com`
      *before* starting Caddy
- [ ] Docker installed (`curl -fsSL https://get.docker.com | sh`)
- [ ] 2 GB swap if the box has ≤2 GB RAM
- [ ] `.env` created with a real `POSTGRES_PASSWORD` and `HAKUTAKU_DOMAIN`
      (`openssl rand -base64 32` for secrets)
- [ ] Postgres **not** port-mapped in `compose.yaml` — internal network only

---

## 5. Publish an image

The readme's old update instructions were `docker compose pull && docker compose
up -d`, but no image is published anywhere — the only compose file builds from
source. Either publish to GHCR from CI and use `image:` in `compose.yaml`, or
change the update flow to `git pull && docker compose up -d --build` and accept
building on the server (needs RAM — see the swap item above). This decision
feeds directly into item 6's deploy stage.

---

## 6. CI/CD with Jenkins

Not started. Separate from the backend work above — this automates building and
deploying whatever item 1-5 produce, it doesn't change the API itself.

- [ ] Stand up Jenkins on the VM
- [ ] Define the pipeline stages by hand first, before automating: build → test
      → build Docker image → push → deploy to the VM
- [ ] Decide the deploy mechanism (same fork as item 5): `git pull && docker
      compose up -d --build` on the VM vs. publishing images to a registry
- [ ] Once the manual steps are proven, encode them in a `Jenkinsfile`

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
- [ ] `server/Models/data.cs` still says "This is just sample code" — expected
      to be replaced by item 1's domain-model work.
- [ ] Run `caddy fmt --overwrite` on the Caddyfile — it currently warns about
      formatting on every start.
