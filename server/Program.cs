using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Endpoints.Admin;
using server.Endpoints.Characters;
using server.Endpoints.Health;
using server.Endpoints.PlayerAuth;
using server.Endpoints.Players;
using server.Endpoints.Telemetry;
using server.Hosting;
using server.SEmail;

var builder = WebApplication.CreateBuilder(args);

// tells di how to build the db context when something asks for one
builder.Services.AddDbContext<server.Models.Db>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

// real mail only if an smtp host is set, otherwise just log the message
if (string.IsNullOrWhiteSpace(builder.Configuration[ConfigKeys.SmtpHost]))
    builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// behind caddy every request looks like it came from caddy, which breaks the per-ip rate limits
// and the session ip. only caddy can reach the app in prod, so we trust its forwarded headers
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddAppRateLimiting();
builder.Services.AddAppServices();

var app = builder.Build();

// apply migrations on startup, then make the owner account if there isn't one yet
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<server.Models.Db>();
    db.Database.Migrate();
    await scope.ServiceProvider.GetRequiredService<OwnerSeeder>().Seed();
}

app.UseApiExceptionHandler();
app.UseForwardedHeaders();
app.UseRateLimiter();

app.MapHealthRoutes();
// public player routes (register, logins, link email, verify, password reset) live in
// endpoints/playerauth and each one has a handle block in the caddyfile. nothing else is public
app.MapPlayerAuthRoutes();

app.MapPlayerRoutes();
app.MapCharacterRoutes();
app.MapTelemetryRoutes();

app.MapAdminRoutes();

// last on purpose, so any other /api path gets a json 404 instead of the vue page
app.MapUnknownApiRoutes();

// makes / serve index.html
app.UseDefaultFiles();

// serves files out of wwwroot
app.UseStaticFiles();

// anything else is a page in the vue app, so send it index.html and let the ui route it
app.MapFallbackToFile("index.html");

app.Run();
