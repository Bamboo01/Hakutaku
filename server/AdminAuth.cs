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
        const int SessionHours = 12;

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

        // Looks up the session for the "Authorization: Bearer <token>" header, if it is still valid.
        public static async Task<AdminSession?> FindActiveSession(Db db, HttpRequest request)
        {
            var header = request.Headers.Authorization.ToString();
            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var tokenHash = HashToken(header[prefix.Length..].Trim());
            var now = DateTime.UtcNow;
            return await db.AdminSessions.FirstOrDefaultAsync(s =>
                s.TokenHash == tokenHash
                && s.RevokedAt == null
                && s.ExpiresAt > now
                && db.AdminUsers.Any(a => a.Id == s.AdminId && a.DisabledAt == null));
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
                var session = new AdminSession
                {
                    AdminId = admin.Id,
                    TokenHash = HashToken(token),
                    Ip = Normalize(http.Connection.RemoteIpAddress),
                    ExpiresAt = DateTime.UtcNow.AddHours(SessionHours),
                };
                db.AdminSessions.Add(session);
                await db.SaveChangesAsync();

                return Results.Ok(new { token, expiresAt = session.ExpiresAt });
            }).RequireRateLimiting("admin-login");

            app.MapPost("/api/admin/logout", async (Db db, HttpContext http) =>
            {
                var session = await FindActiveSession(db, http.Request);
                if (session is null)
                    return Results.Json(new { error = "not logged in" }, statusCode: StatusCodes.Status401Unauthorized);

                session.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return Results.NoContent();
            });
        }
    }
}
