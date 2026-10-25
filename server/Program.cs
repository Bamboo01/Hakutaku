/*!************************************************************************
\file   Program.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file is the entry point of the Hakutaku server. It registers
        the services, applies the migrations, seeds the owner account and
        sets up the request pipeline: error handling, forwarded headers,
        rate limiting, the route groups, then the static files and the Vue
        fallback.

        Calls the setup in Hosting/ and each Map...Routes() in Endpoints/.
        The order matters, especially the /api catch-all being last.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Endpoints.Admin;
using server.Endpoints.Characters;
using server.Endpoints.Health;
using server.Endpoints.PlayerAuth;
using server.Endpoints.Players;
using server.Endpoints.Telemetry;
using server.Hosting;

var builder = WebApplication.CreateBuilder(args);

// tells di how to build the db context when something asks for one
builder.Services.AddDbContext<server.Models.Db>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

builder.Services.AddEmailSender(builder.Configuration);
builder.Services.AddProxyHeaders();
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
