using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;
using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;

namespace server
{
    public static class AdminAuth
    {
        const string OwnerUsername = "admin";
        internal const string CookieName = "hakutaku_admin_session";

        // Absolute lifetime: the session dies at this point no matter how active it is.
        internal const int AbsoluteHours = 12;
        // Idle timeout: each authenticated request pushes this forward, capped at the
        // absolute expiry. A forgotten tab dies on its own after this long unused.
        internal const int IdleMinutes = 30;

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

        internal static IPAddress? Normalize(IPAddress? ip) =>
            ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip;

        internal static string RoleName(AdminRole role) => role == AdminRole.Owner ? "owner" : "admin";

        internal static CookieOptions SessionCookieOptions(HttpRequest request, DateTime expires) => new()
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
        internal static async Task<(AdminUser? Owner, IResult? Error)> RequireOwner(Db db, HttpContext http)
        {
            var found = await FindActiveSession(db, http);
            if (found is null)
                return (null, Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized));
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
                    return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);
                return await next(context);
            });
            return builder;
        }
    }
}
