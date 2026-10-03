# Practices and exposure

The rules the project actually holds to, and an honest account of what is
exposed right now.

## Default-deny is the posture

**Nothing is public unless a line of config says it is.** Caddy's site block
ends with a catch-all that answers `404`, and every public route needs its own
`handle` block added deliberately.

```caddyfile
handle /Health {
    reverse_proxy app:8080
}

handle {
    respond 404
}
```

Two things follow from this, and both are intentional:

- Adding a public route is a **visible diff** in `caddy/Caddyfile`, which
  someone has to review.
- Forgetting to add one fails *closed*. The worst case is "my new endpoint 404s
  in production", not "my new endpoint is on the internet".

!!! question "Why 404 and not 403?"
    A `403` confirms the path exists. A `404` with an empty body is
    indistinguishable from a route that was never written, so scanning the
    public domain tells an attacker nothing about the admin panel — not even
    that there is one.

## What is exposed, right now

| Surface | Public? | Reachable how |
|---|---|---|
| `GET /Health` | **Yes** | `https://51.79.242.169.nip.io/Health` |
| `POST /api/players/register`, `POST /api/players/login` | **Yes** | `https://51.79.242.169.nip.io/api/players/...` — rate-limited, each returns a player session token |
| `POST /api/players/link/email`, `email/verify`, `email/resend` | **Yes** | `https://51.79.242.169.nip.io/api/players/...` — need a player token; verify and resend are rate-limited |
| `POST /api/players/password/forgot`, `password/reset` | **Yes** | `https://51.79.242.169.nip.io/api/players/password/...` — unauthenticated, rate-limited, tied to a mailed code |
| The admin UI | No | SSH tunnel only |
| `/api/admin/*` | No | SSH tunnel only |
| `/api/players` (list/create), `/api/characters`, `/api/events` | No | SSH tunnel only, *and* admin-gated |
| Postgres | No | Docker network only |
| Jenkins | No | SSH tunnel on a separate port |
| **This wiki** | **Yes** | `https://docs.51.79.242.169.nip.io` |

So there are exactly **three** kinds of public surface, and each has a stated reason:

- **`/Health`** — the Jenkins smoke test curls it over HTTPS after every deploy.
- **The player-auth routes** (`register`, `login`, `link/email`, the email verification pair and the password reset pair) — a player has to be able to identify themselves, and recover an account, before any other auth can exist. They are the only game-data paths with their own `handle` blocks in `caddy/Caddyfile`. Everything unauthenticated among them is rate-limited per IP; `link/email`, `email/verify` and `email/resend` are gated by a player session token.
- **The wiki** — onboarding is useless if reading it requires an SSH tunnel.

!!! warning "The wiki describes the system's internals"
    Internal ports, the tunnel, the deploy mechanism, and the fact that
    `/Health` is the only public API route. That is a deliberate trade: the team
    can read it without setup. If it stops being an acceptable trade, Caddy
    `basic_auth` on the docs site block is the whole fix — see
    [Caddy](../components/caddy.md#the-docs-subdomain).

## The game-data endpoints are a stopgap

The list/create endpoints on `/api/players`, plus all of `/api/characters` and
`/api/events`, still sit behind `.RequireAdmin()`. That is **not** the intended
end state — game servers and players are meant to reach them without an admin
session. It is there because the alternative was leaving them open to the
internet while no player-facing auth exists.

The player-auth routes are the first crack in that stopgap. A player can now
register, link and verify an email, log in and reset a password, and gets a
session token, but that token only unlocks the email routes. Nothing else is any
less locked down than before.

Two changes are planned and have not happened yet:

- **The paths become singular.** `/api/players` → `/api/player`, and likewise
  for the others.
- **They become public**, with their own auth — a server key for game servers,
  player credentials for players. Neither exists.

!!! note "Build against reality, document the intent"
    Write code against the paths as they are today. When they change, it will be
    a deliberate migration with the [API reference](../reference/api.md) updated
    in the same commit.

## Secrets

| Secret | Where it lives | In git? |
|---|---|---|
| Production Postgres password | `.env` on the VM, and `/var/lib/jenkins/hakutaku.env` | No — `.env` is gitignored |
| Production domain | same | No |
| First owner password | `HAKUTAKU_ADMIN_PASSWORD`, or generated and logged once | No |
| Dev Postgres password | `hakutaku`, hardcoded in `compose.dev.yaml` and `appsettings.Development.json` | **Yes, on purpose** |

The dev credentials are in source control deliberately: they only ever reach a
container bound to `127.0.0.1` on your own machine, and the alternative is every
new teammate hitting a connection error on their first run.

!!! danger "`compose.dev.yaml` must never be deployed"
    Hardcoded credentials, no TLS, and Postgres published to the host. The file
    header says so, and so does `TODO.md`. Production is `compose.yaml`.

### Rules for secrets

- Never commit a real `.env`. Copy `.env.example` and edit the copy.
- Generate them properly: `openssl rand -base64 32`.
- A secret that reaches a log is burned. Rotate it rather than hoping.

## Credentials and sessions

These are settled decisions, not preferences. Changing any of them is a
security change and should be discussed first.

| Rule | Why |
|---|---|
| Passwords are **Argon2id** at OWASP minimums (19 MiB, t=2, p=1) | Current best practice; the salt and parameters are inside the encoded hash, so there is nothing separate to store |
| Session tokens are 32 random bytes; **only their SHA-256 is stored** | A database dump yields no usable sessions |
| The cookie is `HttpOnly` + `SameSite=Strict` | JS cannot read it, so XSS cannot exfiltrate it; strict same-site blocks CSRF |
| Login answers one identical `401` for wrong password, unknown user and disabled account | Does not leak which usernames exist |
| An unknown username still runs a hash verification against a dummy | A miss costs the same time as a wrong password, so timing does not leak either |
| Two expiries: 30 min idle, 12 h absolute | A forgotten tab dies; a stolen cookie cannot be kept alive forever |

Full detail in [Auth and sessions](../components/auth.md).

## Nothing is hard-deleted

The convention across the admin tables: **deactivate, do not delete.**

- Deactivating an admin sets `disabled_at` and revokes their sessions. The row
  stays.
- Logging out sets `revoked_at` on the session. The row stays.
- There is no restore endpoint, and no `DELETE` on game data at all.

!!! warning "Deactivation burns the username for good"
    The unique index on `lower(username)` covers disabled rows too, so a
    deactivated admin's username can never be reused. The UI says so in its
    confirmation prompt. This is a consequence of soft delete, not a separate
    decision — worth knowing before you deactivate `admin2`.

The owner account can never be deactivated through the API, whoever asks. That
guard is what stops someone locking the whole team out: the seeder only runs
when there are **zero** admins, so a disabled sole owner could not be recovered
over HTTP at all.

## Treat IDs as opaque

Player, character and event IDs are GUIDs today. Planning documents target
`bigint` keys instead.

**So never parse, format, pad or assume the length of an ID** — in the
frontend, in the SDK, or in the simulator. Pass them through as strings. That
way the eventual key migration is a server-side change rather than a hunt
through every client.

## Code conventions

### Comments explain *why*

The codebase is commented at a specific density: enough to explain a decision
that would otherwise look arbitrary, not a narration of the code. Match it.

```csharp
// Materialised before projecting so RoleName stays reusable -- EF can't
// translate a local method into SQL. The table is tiny, so this is fine.
var admins = await db.AdminUsers.OrderBy(a => a.Id).ToListAsync();
```

That comment is worth keeping, because the next reader's first instinct is to
"optimise" it back into a single SQL projection, which does not compile.

### Everything else

- **Migrations** are EF-generated and keep their timestamp names. Never
  hand-edit one that has been applied anywhere.
- **Admin tables** are snake_case with `bigint` keys. Follow that for new
  tables, not the PascalCase of the older game tables.
- **The frontend has one fetch wrapper.** All HTTP goes through `api.ts` so a
  dead session is handled in one place. Do not call `fetch` from a page.
- **One route table.** The nav menu and the auth guard are both derived from the
  array in `router.ts`. Adding a page is one entry there.
- **Server-side validation is the real validation.** The UI mirrors the server's
  rules to keep the forms honest, never as the enforcement point.

## Known gaps

Deliberately listed rather than quietly omitted:

- No player-facing auth, so game data is admin-gated as a stopgap.
- No audit log table, despite the concept appearing in planning docs.
- `admin_sessions.ip` is recorded but never compared against anything.
- `admin_users.totp_secret` exists in the schema and is unused by any code.
- Deactivation records no `disabled_by`, and re-deactivating overwrites
  `disabled_at`.
- Deactivation's two writes are not in one transaction: sessions are revoked,
  then the disable is saved.
- Migrations run on startup, so several app instances against one database would
  race.
- No test suite anywhere, so CI has no test stage.
