using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using server.Models;

namespace server
{
    public record LoginRequest(string? Username, string? Password);
    public record CreateAdminRequest(string? Username, string? Password);

    public static class AdminAuth
    {
        const string OwnerUsername = "admin";
        const string CookieName = "hakutaku_admin_session";

        // Absolute lifetime: the session dies at this point no matter how active it is.
        const int AbsoluteHours = 12;
        // Idle timeout: each authenticated request pushes this forward, capped at the
        // absolute expiry. A forgotten tab dies on its own after this long unused.
        const int IdleMinutes = 30;

        // OWASP's minimum Argon2id settings: 19 MiB of memory, 2 iterations, 1 lane.
        const int TimeCost = 2;
        const int MemoryCostKib = 19456;
        const int Lanes = 1;
        const int HashLength = 32;

        // Checked against when the username is unknown, so a miss takes as long as a wrong password.
        // internal: player email login (PlayerAuth.cs) shares the hashing and token helpers.
        internal static readonly string DummyHash = HashPassword("dummy-password-for-timing");

        public static string HashPassword(string password) =>
            Argon2.Hash(password, TimeCost, MemoryCostKib, Lanes, Argon2Type.HybridAddressing, HashLength);

        internal static bool VerifyPassword(string encodedHash, string password) =>
            Argon2.Verify(encodedHash, password);

        internal static byte[] HashToken(string token) =>
            SHA256.HashData(Encoding.UTF8.GetBytes(token));

        static IPAddress? Normalize(IPAddress? ip) =>
            ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip;

        static string RoleName(AdminRole role) => role == AdminRole.Owner ? "owner" : "admin";

        static CookieOptions SessionCookieOptions(HttpRequest request, DateTime expires) => new()
        {
            HttpOnly = true,
            // Only marked Secure when the request itself arrived over HTTPS, so the
            // cookie still works for local dev (plain HTTP) but is Secure in production,
            // where UseForwardedHeaders() reports the scheme Caddy terminated.
            Secure = request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = expires,
            Path = "/",
        };

        // Creates the owner account on first start, when no admins exist yet.
        public static async Task SeedOwner(Db db, IConfiguration config, ILogger logger)
        {
            if (await db.AdminUsers.AnyAsync()) return;

            var password = config["HAKUTAKU_ADMIN_PASSWORD"];
            var generated = string.IsNullOrWhiteSpace(password);
            if (generated) password = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(18));

            db.AdminUsers.Add(new AdminUser
            {
                Username = OwnerUsername,
                PwHash = HashPassword(password!),
                Role = AdminRole.Owner,
            });
            await db.SaveChangesAsync();

            if (generated)
                logger.LogWarning("Created the owner account '{Username}' with the generated password {Password}. It is shown only once.", OwnerUsername, password);
            else
                logger.LogInformation("Created the owner account '{Username}'.", OwnerUsername);
        }

        // Looks up the session cookie, if any, and checks it is still valid (not revoked,
        // not past its absolute or idle expiry, and its admin isn't disabled). On success,
        // pushes the idle expiry forward -- this is what "each request extends the
        // session" means, and every caller of this method gets that for free.
        public static async Task<(AdminSession Session, AdminUser Admin)?> FindActiveSession(Db db, HttpContext http)
        {
            if (!http.Request.Cookies.TryGetValue(CookieName, out var token) || string.IsNullOrEmpty(token))
                return null;

            var tokenHash = HashToken(token);
            var now = DateTime.UtcNow;
            var session = await db.AdminSessions.FirstOrDefaultAsync(s =>
                s.TokenHash == tokenHash
                && s.RevokedAt == null
                && s.ExpiresAt > now
                && s.IdleExpiresAt > now);
            if (session is null) return null;

            var admin = await db.AdminUsers.FindAsync(session.AdminId);
            if (admin is null || admin.DisabledAt is not null) return null;

            var nextIdle = now.AddMinutes(IdleMinutes);
            session.IdleExpiresAt = nextIdle < session.ExpiresAt ? nextIdle : session.ExpiresAt;
            await db.SaveChangesAsync();

            return (session, admin);
        }

        // Requires an active session belonging to the owner specifically (not just any
        // admin) -- per the TDD, admin creation/deletion goes through the owner only.
        static async Task<(AdminUser? Owner, IResult? Error)> RequireOwner(Db db, HttpContext http)
        {
            var found = await FindActiveSession(db, http);
            if (found is null)
                return (null, Results.Json(new { error = "not logged in" }, statusCode: StatusCodes.Status401Unauthorized));
            if (found.Value.Admin.Role != AdminRole.Owner)
                return (null, Results.Json(new { error = "owner only" }, statusCode: StatusCodes.Status403Forbidden));
            return (found.Value.Admin, null);
        }

        // Chainable like .RequireRateLimiting(...): app.MapGet(...).RequireAdmin(). Lets
        // in any logged-in admin or the owner; use RequireOwner (above) inside a handler
        // when only the owner specifically should be allowed.
        public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        {
            builder.AddEndpointFilter(async (context, next) =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<Db>();
                var found = await FindActiveSession(db, context.HttpContext);
                if (found is null)
                    return Results.Json(new { error = "not logged in" }, statusCode: StatusCodes.Status401Unauthorized);
                return await next(context);
            });
            return builder;
        }

        public static void MapAdminEndpoints(this WebApplication app)
        {
            app.MapPost("/api/admin/login", async (Db db, HttpContext http, LoginRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password)
                    || request.Username.Length > 254 || request.Password.Length > 256)
                    return Results.BadRequest(new { error = "username and password are required" });

                var username = request.Username.Trim().ToLowerInvariant();
                var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.Username.ToLower() == username);

                var passwordOk = VerifyPassword(admin?.PwHash ?? DummyHash, request.Password);
                if (admin is null || admin.DisabledAt is not null || !passwordOk)
                    return Results.Json(new { error = "invalid credentials" }, statusCode: StatusCodes.Status401Unauthorized);

                var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
                var now = DateTime.UtcNow;
                var session = new AdminSession
                {
                    AdminId = admin.Id,
                    TokenHash = HashToken(token),
                    Ip = Normalize(http.Connection.RemoteIpAddress),
                    ExpiresAt = now.AddHours(AbsoluteHours),
                    IdleExpiresAt = now.AddMinutes(IdleMinutes),
                };
                db.AdminSessions.Add(session);
                await db.SaveChangesAsync();

                http.Response.Cookies.Append(CookieName, token, SessionCookieOptions(http.Request, session.ExpiresAt));
                return Results.NoContent();
            }).RequireRateLimiting("admin-login");

            app.MapPost("/api/admin/logout", async (Db db, HttpContext http) =>
            {
                var found = await FindActiveSession(db, http);
                if (found is null)
                    return Results.Json(new { error = "not logged in" }, statusCode: StatusCodes.Status401Unauthorized);

                found.Value.Session.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
                return Results.NoContent();
            });

            app.MapGet("/api/admin/me", async (Db db, HttpContext http) =>
            {
                var found = await FindActiveSession(db, http);
                if (found is null)
                    return Results.Json(new { error = "not logged in" }, statusCode: StatusCodes.Status401Unauthorized);

                return Results.Ok(new
                {
                    username = found.Value.Admin.Username,
                    email = found.Value.Admin.Email,
                    role = RoleName(found.Value.Admin.Role),
                });
            });

            // Owner only. Includes deactivated admins: the page needs disabledAt to show
            // state, and the id to target a deactivation. Added by Sherwyn for the admins page.
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

            // Owner only. Always creates a regular admin -- there's only ever one owner,
            // the one seeded at first start, per the TDD ("one default super admin account").
            app.MapPost("/api/admin/admins", async (Db db, HttpContext http, CreateAdminRequest request) =>
            {
                var (owner, error) = await RequireOwner(db, http);
                if (error is not null) return error;

                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password)
                    || request.Username.Length > 254 || request.Password.Length < 8 || request.Password.Length > 256)
                    return Results.BadRequest(new { error = "username is required and password must be 8-256 characters" });

                var admin = new AdminUser
                {
                    Username = request.Username.Trim(),
                    PwHash = HashPassword(request.Password),
                    Role = AdminRole.Admin,
                    CreatedBy = owner!.Id,
                };
                db.AdminUsers.Add(admin);

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" })
                {
                    return Results.Json(new { error = "username already taken" }, statusCode: StatusCodes.Status409Conflict);
                }

                return Results.Ok(new { id = admin.Id, username = admin.Username, role = RoleName(admin.Role) });
            });

            // Owner only. One-way: sets disabled_at, doesn't hard-delete (matches the
            // TDD's "nothing is hard-deleted" convention) and there's no restore endpoint
            // since the team doesn't need one. Also kills that admin's active sessions,
            // so a deactivation takes effect immediately rather than waiting for their
            // cookie to expire on its own.
            app.MapDelete("/api/admin/admins/{id}", async (Db db, HttpContext http, long id) =>
            {
                var (owner, error) = await RequireOwner(db, http);
                if (error is not null) return error;

                var target = await db.AdminUsers.FindAsync(id);
                if (target is null)
                    return Results.NotFound(new { error = "no such admin" });
                if (target.Role == AdminRole.Owner)
                    return Results.Json(new { error = "the owner account can't be deactivated" }, statusCode: StatusCodes.Status403Forbidden);

                var now = DateTime.UtcNow;
                target.DisabledAt = now;
                await db.AdminSessions
                    .Where(s => s.AdminId == target.Id && s.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
                await db.SaveChangesAsync();

                return Results.NoContent();
            });
        }
    }
}
