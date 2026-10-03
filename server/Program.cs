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

// Real mail only when an SMTP host is configured; otherwise messages are just logged.
if (string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Host"]))
    builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

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
    // Public and unauthenticated (see PlayerAuth.cs), so this is the only thing standing
    // between registration and someone hammering it to fill the table.
    o.AddPolicy("player-register", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    // Email login is public too, and each attempt costs an Argon2 hash, so it gets the
    // same per-IP limit as admin login. Separate policy so one doesn't use up the other.
    o.AddPolicy("player-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    // The mail routes (verify, resend, forgot, reset). Each one either sends an email or
    // lets someone guess a code, so they share one per-IP limit, apart from login's.
    o.AddPolicy("player-mail", context => RateLimitPartition.GetFixedWindowLimiter(
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
// The public player routes (register, link/email, login) are in PlayerAuth.cs.
// caddy/Caddyfile has a handle block for each; nothing else here is public.
app.MapPlayerEndpoints();

// Admin-or-owner only. Players can now log in, but a player session doesn't unlock
// these yet -- they are the admin's view, not the player API.
// Lists only which providers each player has linked, never the device IDs or emails:
// a device ID works like a password, so it shouldn't show up in an admin listing.
app.MapGet("/api/players", async (server.Models.Db db) =>
{
    var players = await db.Players
        .OrderBy(p => p.Id)
        .Select(p => new
        {
            id = p.Id,
            xp = p.Xp,
            providers = db.PlayerIdentities.Where(i => i.PlayerId == p.Id).Select(i => i.Provider).ToList(),
        })
        .ToListAsync();
    return Results.Ok(players.Select(p => new
    {
        p.id,
        p.xp,
        providers = p.providers.Select(PlayerAuth.ProviderName).Order().ToList(),
    }));
}).RequireAdmin();
// Admin-created players get a device identity only if a deviceId is given, so a
// player made this way can't log in until they have one.
app.MapPost("/api/players", async (server.Models.Db db, AdminCreatePlayerRequest request) =>
{
    var deviceId = request.DeviceId?.Trim();
    if (deviceId is not null && (deviceId.Length == 0 || deviceId.Length > 254))
        return Results.BadRequest(new { error = "deviceId must be 1-254 characters when given" });

    var player = new server.Models.Player { Xp = request.Xp };
    db.Players.Add(player);
    if (deviceId is not null)
        db.PlayerIdentities.Add(new server.Models.PlayerIdentity
        {
            Player = player,
            Provider = server.Models.PlayerProvider.Device,
            Subject = deviceId,
        });

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
    {
        return Results.Json(new { error = "a player with that deviceId already exists" }, statusCode: StatusCodes.Status409Conflict);
    }
    return Results.Ok(new { id = player.Id, xp = player.Xp });
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
