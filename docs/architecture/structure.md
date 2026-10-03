# Project structure

## The repo at a glance

```text
Hakutaku/
├── server/                  ASP.NET Core 10 API (also serves the built UI)
│   ├── Program.cs           Entry point: DI, middleware, and most endpoints
│   ├── AdminAuth.cs         Everything auth — hashing, sessions, admin CRUD
│   ├── Models/
│   │   ├── data.cs          Player, Character, TelemetryEvent + the DbContext
│   │   └── Admin.cs         AdminUser, AdminSession, AdminRole
│   ├── Migrations/          EF Core migration history (5 so far)
│   ├── appsettings.json             Base config
│   └── appsettings.Development.json Dev connection string (host, not Docker)
│
├── web/                     Vue 3 admin UI
│   └── src/
│       ├── main.ts          createApp + router + the 401 handler
│       ├── App.vue          Shell: hamburger nav, theme toggle, <router-view>
│       ├── router.ts        Routes + the beforeEach auth guard
│       ├── api.ts           The single fetch wrapper every request goes through
│       ├── style.css        Theme tokens (matcha green, light + dark)
│       ├── pages/           One file per route
│       └── components/      Pieces shared across pages
│
├── caddy/Caddyfile          Reverse proxy config — default-deny
├── compose.yaml             Production stack
├── compose.dev.yaml         Local stack (never deploy this)
├── Dockerfile               3-stage build: UI → API → slim runtime
├── Jenkinsfile              Deploy + smoke test on merge to master
├── docs/                    This wiki
│
├── sdk/                     PlayFab-style C# client library (netstandard2.1, for Unity)
└── simulator/               .NET console app: mock players, through the SDK
```

## Tech stack, and why each piece

| Layer | Choice | Why this one |
|---|---|---|
| API | **ASP.NET Core 10**, minimal APIs | The team is a C# team. Minimal APIs skip the controller/attribute ceremony, which matters when the whole API is ~15 endpoints. |
| ORM | **EF Core 10** + **Npgsql** | Code-first migrations mean the schema lives in version control as C#, and `Database.Migrate()` applies it on boot — no manual DB step in anyone's workflow. |
| Database | **PostgreSQL 18** | Free, excellent JSON support for telemetry payloads, and functional indexes (`lower(username)`) that we actually depend on for case-insensitive login. |
| Hashing | **Isopoh.Cryptography.Argon2** | Argon2id is the current OWASP recommendation for password storage. Pure managed C#, no native dependency to ship in the container. |
| UI framework | **Vue 3** with `<script setup>` + TypeScript | Lower ceremony than React for a small admin panel, and single-file components keep a page's markup, logic and styles in one file. |
| UI build | **Vite 8** | Instant dev server, and `vue-tsc` in the build step means a type error fails CI rather than reaching production. |
| UI routing | **vue-router 5** | The route table is also where auth metadata lives, so the guard and the nav menu both derive from one array. |
| Styling | **Hand-written CSS** with custom properties | Deliberately no component library. The panel is small, and a theme built from ~12 CSS variables is easier to read and rewrite than a framework's override system. |
| Proxy / TLS | **Caddy 2** | Automatic HTTPS with zero certificate code. The site config is about ten lines. |
| Containers | **Docker Compose** | One file describes the whole stack, and the same `Dockerfile` builds locally and in CI. |
| CI/CD | **Jenkins** (on the VM) | Already running on the team VM for other coursework, so it was free. Builds on the server, so there is no image registry to manage. |

## Design decisions worth knowing

### One process serves both the API and the UI

The `Dockerfile` builds the Vue app to static files and copies them into the
server's `wwwroot`. The same ASP.NET process then serves `/api/*` **and** the
UI.

This is not laziness — it is what makes cookie auth simple. The session cookie
is `HttpOnly` and `SameSite=Strict`, so JavaScript can never read it and the
browser only sends it to its own origin. One origin means no CORS config
anywhere, and no token plumbing in the frontend. Split the UI onto its own
origin and login breaks.

!!! warning "Middleware order is load-bearing"
    In `Program.cs`, `UseDefaultFiles()` / `UseStaticFiles()` /
    `MapFallbackToFile("index.html")` come **after** the API routes.
    Static-file middleware skips any request that already matched an endpoint,
    so **never map `/` to an endpoint** — it would shadow the whole UI. There is
    a comment in the file saying exactly this.

### No controllers, no repositories, no service layer

Endpoints are lambdas registered directly on the app. A handler takes the
`DbContext` by injection, does its query, and returns a result:

```csharp
app.MapGet("/api/players", async (server.Models.Db db) =>
    await db.Players.ToListAsync()).RequireAdmin();
```

There is no repository interface wrapping EF Core, and no service class in
between. At this size that indirection would be pure cost. If a handler ever
grows past a screenful, pull it into a method — that is the whole pattern.

The one exception is auth, which lives in `AdminAuth.cs` as a static class with
`MapAdminEndpoints()`. It earned its own file because the logic is genuinely
shared (session lookup, role checks) and genuinely subtle.

### Auth is an endpoint filter, not an attribute

There is no ASP.NET Identity and no `[Authorize]`. Two primitives do the work:

- `.RequireAdmin()` — a chainable extension method you append to a route, like
  `.RequireRateLimiting(...)`. Lets in any signed-in admin **or** the owner.
- `RequireOwner(db, http)` — called *inside* a handler when only the owner
  should get through, because it needs to return a specific `403`.

Both are in `AdminAuth.cs`. See [Auth and sessions](../components/auth.md).

### Migrations run themselves

`Program.cs` calls `db.Database.Migrate()` at startup, inside a DI scope,
before the app starts handling requests. Nobody runs `dotnet ef database
update` as part of normal work.

!!! note "This has a limit"
    It is correct for one instance. If the stack is ever scaled to several app
    containers against one database, they will race to apply migrations on boot.
    Fine today; a real concern the day a second instance appears.

### Two entity styles, on purpose

The schema has two visibly different conventions, because they arrived at
different times:

| | Game data | Admin data |
|---|---|---|
| Tables | `Players`, `Characters`, `TelemetryEvents` | `admin_users`, `admin_sessions` |
| Naming | PascalCase (EF default) | snake_case (explicit) |
| Primary key | `uuid` (GUID) | `bigint` identity |

The admin tables follow the TDD's conventions; the game tables predate them and
still use EF's defaults. [Database](../components/database.md) covers the
consequences, and why you should treat player ids as opaque strings in the
frontend.

## What is deliberately missing

Worth knowing so you do not go looking:

- **No test suite**, in either `server/` or `web/`. The Jenkins pipeline has no
  test stage because there is nothing to run.
- **No player-facing auth.** `/api/players`, `/api/characters` and `/api/events`
  are gated behind *admin* login as a stopgap. They are meant to be public
  eventually — see [Practices](practices.md).
- **No `PUT` or `DELETE`** on game data, and no paging or filtering: list
  endpoints return the entire table.
- **No audit log table**, despite the concept appearing in planning docs. The
  nearest real data is `admin_sessions`, which is login history, not an audit
  trail.
- **No CORS configuration**, and none is wanted — see above.
