using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using server;
using server.Constants;
using server.Endpoints.Admin;
using server.Endpoints.Characters;
using server.Endpoints.Players;
using server.Endpoints.Telemetry;

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
if (string.IsNullOrWhiteSpace(builder.Configuration[ConfigKeys.SmtpHost]))
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

    // Every policy is the same rule: a fixed number of requests per client IP in each window.
    // They are separate policies so each group of routes has its own allowance (see RateLimits).
    void AddPerIpPolicy(string name) => o.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? RateLimits.UnknownClient,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = RateLimits.PermitLimit, Window = RateLimits.Window }));

    AddPerIpPolicy(RateLimits.AdminLogin);
    AddPerIpPolicy(RateLimits.PlayerRegister);
    AddPerIpPolicy(RateLimits.PlayerLogin);
    AddPerIpPolicy(RateLimits.PlayerDeviceLogin);
    AddPerIpPolicy(RateLimits.PlayerMail);
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

app.MapPlayerRoutes();
app.MapCharacterRoutes();
app.MapTelemetryRoutes();

app.MapAdminRoutes();

// UseDefaultFiles rewrites a request for / to /index.html
app.UseDefaultFiles();

// UseStaticFiles serves anything found in wwwroot
app.UseStaticFiles();

// If we go to a broken link, it reroutes us back to index.html
app.MapFallbackToFile("index.html");

app.Run();
