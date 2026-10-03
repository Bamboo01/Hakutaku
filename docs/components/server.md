# Server

ASP.NET Core 10, minimal APIs, two files that matter:

| File | Holds |
|---|---|
| `server/Program.cs` | DI, the middleware pipeline, and the game-data endpoints |
| `server/AdminAuth.cs` | Everything auth — covered in [Auth and sessions](auth.md) |

`Program.cs` reads top to bottom as the whole request pipeline. Start there.

## Project setup

```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<RootNamespace>server</RootNamespace>
```

Nullable reference types are **on**, so `string?` versus `string` in a request
record is a real signal about what may be missing. Three packages:

| Package | Version | For |
|---|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 | The Postgres provider |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.12 | `dotnet ef` tooling (build-time only) |
| `Isopoh.Cryptography.Argon2` | 2.0.0 | Password hashing |

## Startup, in order

### 1. The DbContext

```csharp
builder.Services.AddDbContext<server.Models.Db>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));
```

Registers `Db` as a scoped service, so any handler can take it as a parameter
and get one per request. See
[Database](database.md#connection-strings-two-readers) for which connection
string wins.

### 2. Forwarded headers

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
```

Behind Caddy, every request would otherwise appear to come from Caddy. That
would break the per-IP login rate limit (one shared bucket for the world) and
the session cookie's `Secure` flag (Caddy speaks plain HTTP internally, so
`request.IsHttps` would be false).

Clearing the known-proxy lists means "trust whatever sent this". That is only
safe because nothing but Caddy can reach the app — which is why `compose.yaml`
binds the app's port to `127.0.0.1`.

### 3. The rate limiter

```csharp
o.AddPolicy("admin-login", context => RateLimitPartition.GetFixedWindowLimiter(
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
```

Five login attempts per minute per IP, fixed window. Applied to one endpoint
only, with `.RequireRateLimiting("admin-login")`.

!!! note "The 429 has no body"
    Unlike the hand-written `400`/`401`/`403` responses, which return
    `{ "error": "…" }`, the limiter's rejection is an empty `429`. Clients must
    not assume a JSON body — `web/src/api.ts` handles this explicitly.

### 4. Migrate and seed

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<server.Models.Db>();
    db.Database.Migrate();
    await server.AdminAuth.SeedOwner(db, app.Configuration, app.Logger);
}
```

Runs before the app serves traffic. The explicit scope is needed because `Db` is
scoped and there is no request to hang one off yet.

### 5. Middleware

```csharp
app.UseForwardedHeaders();
app.UseRateLimiter();
```

!!! warning "`UseForwardedHeaders` must stay first"
    Both the rate-limit partition and `request.IsHttps` read the values it
    rewrites. Move it after `UseRateLimiter()` and the limiter silently buckets
    every request under Caddy's IP.

## The endpoint pattern

A handler is a lambda. It takes what it needs by injection, queries, returns:

```csharp
app.MapGet("/api/players", async (server.Models.Db db) =>
    await db.Players.ToListAsync()).RequireAdmin();

app.MapPost("/api/players", async (server.Models.Db db, server.Models.Player p) =>
{
    db.Players.Add(p);
    await db.SaveChangesAsync();
    return Results.Ok(p);
}).RequireAdmin();
```

No controllers, no repository interfaces, no service layer. A `Player` parameter
is bound from the JSON body automatically.

`TelemetryEvent` is the one with server-side behaviour — the client's timestamp
is always discarded:

```csharp
e.Timestamp = DateTime.UtcNow;
```

### Current endpoints

| Path | Methods | Guard |
|---|---|---|
| `/Health` | GET | None — public through Caddy |
| `/api/players` | GET, POST | `.RequireAdmin()` |
| `/api/characters` | GET, POST | `.RequireAdmin()` |
| `/api/events` | GET, POST | `.RequireAdmin()` |
| `/api/admin/*` | various | See [Auth](auth.md) |

!!! info "These paths are going to change"
    `/api/players` is intended to become `/api/player` (singular), and the game
    data endpoints are meant to be **publicly reachable** with their own auth
    rather than admin-gated. Neither has happened. Code against what is there
    today; see [Practices](../architecture/practices.md#the-game-data-endpoints-are-a-stopgap).

## Static files and the SPA fallback

The last three lines of `Program.cs`, and the order is critical:

```csharp
app.UseDefaultFiles();                    // rewrites / to /index.html
app.UseStaticFiles();                     // serves anything in wwwroot
app.MapFallbackToFile("index.html");      // unknown paths -> the Vue app
```

These come **after** every API route, which is what makes the arrangement work:
static-file middleware skips any request that already matched an endpoint.

!!! danger "Never map `/` to an endpoint"
    It would shadow the entire UI, because the route would match before
    `UseDefaultFiles()` ever ran. There is a comment in `Program.cs` saying
    exactly this. If you need a root-ish API route, put it under `/api/`.

`MapFallbackToFile` is what makes client-side routing work: a browser asking for
`/admins` directly gets `index.html`, and vue-router takes it from there.

## Adding an endpoint

1. **Pick the guard.** `.RequireAdmin()` for any signed-in admin;
   `RequireOwner(db, http)` inside the handler when only the owner should pass.
   Nothing is unauthenticated by default.
2. **Register it before the static-file calls.** Anywhere above
   `UseDefaultFiles()`.
3. **Return a typed result.** `Results.Ok(...)`, `Results.NoContent()`,
   `Results.BadRequest(new { error = "…" })`. Keep the `{ error }` shape —
   `web/src/api.ts` reads that field.
4. **Does it need to be public?** Then it also needs a `handle` block in
   `caddy/Caddyfile`, and admin-session auth is the wrong gate for it.
5. **Update [the API reference](../reference/api.md)** in the same commit.

A worked example, from `AdminAuth.cs`:

```csharp
app.MapGet("/api/admin/admins", async (Db db, HttpContext http) =>
{
    var (_, error) = await RequireOwner(db, http);
    if (error is not null) return error;

    // Materialised before projecting so RoleName stays reusable -- EF can't
    // translate a local method into SQL. The table is tiny, so this is fine.
    var admins = await db.AdminUsers.OrderBy(a => a.Id).ToListAsync();
    return Results.Ok(admins.Select(a => new
    {
        id = a.Id,
        username = a.Username,
        role = RoleName(a.Role),
        createdAt = a.CreatedAt,
        disabledAt = a.DisabledAt,
    }));
});
```

Two things to copy: the guard runs first and returns early, and the projection
decides the JSON shape explicitly rather than serialising the entity. The
latter is what keeps `pw_hash` from ever reaching a response.

## Configuration

Standard ASP.NET precedence — later wins:

1. `appsettings.json`
2. `appsettings.Development.json` (only when `ASPNETCORE_ENVIRONMENT=Development`)
3. Environment variables

Which is how `ConnectionStrings__Db` from a compose file overrides the dev JSON
inside a container. The double underscore is the nesting separator.

| Variable | Read by | Purpose |
|---|---|---|
| `ConnectionStrings__Db` | `AddDbContext` | The Postgres connection string |
| `HAKUTAKU_ADMIN_PASSWORD` | `SeedOwner` | First owner's password, first start only |
| `Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Password`, `Smtp__From` | `SmtpEmailSender` | Outgoing mail for verification and reset codes. With no host set, `LogEmailSender` logs the message instead |
| `ASPNETCORE_ENVIRONMENT` | The framework | `Development` loads the dev JSON |

Dev ports come from `server/Properties/launchSettings.json`: `5008` for HTTP,
`5009` for the HTTPS profile. Vite proxies to `5008`.

## Known rough edges

- **A bad `playerId` returns a bare `500`.** The foreign-key violation is not
  caught and turned into a `400`.
- **No validation layer.** Only auth does input checking, by hand.
- **List endpoints return the whole table.** No paging, no filtering, no `PUT`
  or `DELETE` on game data.
- **`data.cs` still opens with "This is just sample code".** It is not; the
  comment is stale and the backend owner has it.
- **No tests.** The Jenkins pipeline has no test stage because there is nothing
  to run.
