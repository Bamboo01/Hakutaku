using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using server;

var builder = WebApplication.CreateBuilder(args);

// README:
// dotnet ef migrations add InitialCreate
// Reflection magic that describes schema changes
// Everytime the Hakutaku.Server starts up, connects to postgres, then checks which migrations have been applied
// See the migrations folder after creation

// This just tells the DI container how to build the db instance. 
// Whenever something requests a Db instance (via constructor injection), here's how to construct it.
builder.Services.AddDbContext<server.Models.Db>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

// Behind Caddy every request would otherwise appear to come from Caddy itself, which would make
// the per-IP login limit and the session IP useless. Only Caddy can reach this container in
// production (compose.yaml publishes no port for it), so its forwarded headers are trusted.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("admin-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    // Public and unauthenticated (see the registration endpoint below), so this is the
    // only thing standing between it and someone hammering it to fill the table.
    o.AddPolicy("player-register", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
});

// dotnet ef database update
// Running this command migrates the changes to the database - AKA editing the table to match the schemas inside the serialized db
// But we don't need to do this since we're doing rapid development.
var app = builder.Build();

// This automatically builds it for you!
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<server.Models.Db>();
    db.Database.Migrate();
    await server.AdminAuth.SeedOwner(db, app.Configuration, app.Logger);
}

app.UseForwardedHeaders();
app.UseRateLimiter();

// NOTE: do not map "/" to an endpoint. Static-file middleware skips any request
// that already matched an endpoint, so a route here shadows the Vue UI below.
app.MapGet("/Health", () => Results.Ok( new { health = "ok" } ));
// Public, find-or-create by device ID. This is intentionally the *only* public
// game-data endpoint -- caddy/Caddyfile has a handle block for it specifically.
// No session/token is issued; that's a separate, bigger piece (see TODO.md item 5)
// closer to the TDD's player_identities/player_sessions design. For now this is
// just "does a player for this device exist yet," which is what was asked for.
app.MapPost("/api/players/register", async (server.Models.Db db, PlayerRegisterRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 254)
        return Results.BadRequest(new { error = "deviceId is required" });

    var deviceId = request.DeviceId.Trim();
    var existing = await db.Players.FirstOrDefaultAsync(p => p.DeviceId == deviceId);
    if (existing is not null) return Results.Ok(existing);

    var player = new server.Models.Player { DeviceId = deviceId, Xp = 0 };
    db.Players.Add(player);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
    {
        // Lost a race with another registration for the same device between the
        // lookup above and this save -- fetch whichever row actually won.
        return Results.Ok(await db.Players.FirstAsync(p => p.DeviceId == deviceId));
    }
    return Results.Ok(player);
}).RequireRateLimiting("player-register");

// Admin-or-owner only for now -- there's no separate player-facing auth yet,
// so this is the only thing standing between these endpoints and the public
// internet. See TODO.md for the planned player login.
app.MapGet("/api/players", async (server.Models.Db db) => await db.Players.ToListAsync()).RequireAdmin();
app.MapPost("/api/players", async (server.Models.Db db, server.Models.Player p) =>
{
    db.Players.Add(p);
    await db.SaveChangesAsync();
    return Results.Ok(p);
}).RequireAdmin();

app.MapGet("/api/characters", async (server.Models.Db db) => await db.Characters.ToListAsync()).RequireAdmin();
app.MapPost("/api/characters", async (server.Models.Db db, server.Models.Character c) =>
{
    db.Characters.Add(c);
    await db.SaveChangesAsync();
    return Results.Ok(c);
}).RequireAdmin();

app.MapGet("/api/events", async (server.Models.Db db) => await db.TelemetryEvents.ToListAsync()).RequireAdmin();
app.MapPost("/api/events", async (server.Models.Db db, server.Models.TelemetryEvent e) =>
{
    e.Timestamp = DateTime.UtcNow;
    db.TelemetryEvents.Add(e);
    await db.SaveChangesAsync();
    return Results.Ok(e);
}).RequireAdmin();

app.MapAdminEndpoints();

// UseDefaultFiles rewrites a request for / to /index.html
app.UseDefaultFiles();

// UseStaticFiles serves anything found in wwwroot
app.UseStaticFiles();

// If we go to a broken link, it reroutes us back to index.html
app.MapFallbackToFile("index.html");

app.Run();

record PlayerRegisterRequest(string? DeviceId);
