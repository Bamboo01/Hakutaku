using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;
using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server
{
    public record LoginRequest(string? Email, string? Password);

    public static class AdminAuth
    {
        const string OwnerEmail = "admin";
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

        // Checked against when the email is unknown, so a miss takes as long as a wrong password.
        static readonly string DummyHash = HashPassword("dummy-password-for-timing");

        public static string HashPassword(string password) =>
            Argon2.Hash(password, TimeCost, MemoryCostKib, Lanes, Argon2Type.HybridAddressing, HashLength);

        static bool VerifyPassword(string encodedHash, string password) =>
            Argon2.Verify(encodedHash, password);

        static byte[] HashToken(string token) =>
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
                Email = OwnerEmail,
                PwHash = HashPassword(password!),
                Role = AdminRole.Owner,
            });
            await db.SaveChangesAsync();

            if (generated)
                logger.LogWarning("Created the owner account '{Email}' with the generated password {Password}. It is shown only once.", OwnerEmail, password);
            else
                logger.LogInformation("Created the owner account '{Email}'.", OwnerEmail);
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

        public static void MapAdminEndpoints(this WebApplication app)
        {
            app.MapPost("/api/admin/login", async (Db db, HttpContext http, LoginRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password)
                    || request.Email.Length > 254 || request.Password.Length > 256)
                    return Results.BadRequest(new { error = "email and password are required" });

                var email = request.Email.Trim().ToLowerInvariant();
                var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.Email.ToLower() == email);

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

                return Results.Ok(new { email = found.Value.Admin.Email, role = RoleName(found.Value.Admin.Role) });
            });
        }
    }
}
