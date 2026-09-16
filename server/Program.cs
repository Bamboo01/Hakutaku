using Microsoft.EntityFrameworkCore;

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

// dotnet ef database update
// Running this command migrates the changes to the database - AKA editing the table to match the schemas inside the serialized db
// But we don't need to do this since we're doing rapid development.
var app = builder.Build();

// This automatically builds it for you!
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<server.Models.Db>().Database.Migrate();
}

// NOTE: do not map "/" to an endpoint. Static-file middleware skips any request
// that already matched an endpoint, so a route here shadows the Vue UI below.
app.MapGet("/Health", () => Results.Ok( new { health = "ok" } ));
app.MapGet("/api/players", async (server.Models.Db db) => await db.Players.ToListAsync());
app.MapPost("/api/players", async (server.Models.Db db, server.Models.Player p) =>
{
    db.Players.Add(p);
    await db.SaveChangesAsync();
    return Results.Ok(p);
});

// UseDefaultFiles rewrites a request for / to /index.html
app.UseDefaultFiles();

// UseStaticFiles serves anything found in wwwroot
app.UseStaticFiles();

// If we go to a broken link, it reroutes us back to index.html
app.MapFallbackToFile("index.html");

app.Run();
