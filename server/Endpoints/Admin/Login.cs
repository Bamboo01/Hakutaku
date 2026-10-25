using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Constants;
using server.Hosting;
using server.Models;

namespace server.Endpoints.Admin
{
    public record LoginRequest(string? Username, string? Password);

    public static class Login
    {
        // ipv4 can show up wrapped in ipv6 form (::ffff:1.2.3.4), unwrap it so the session stores
        // the plain address
        static IPAddress? Normalize(IPAddress? ip) =>
            ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip;

        // post /api/admin/login: checks username + password, then sets the session cookie
        public static async Task<IResult> Handle(Db db, TimeProvider time, HttpContext http, LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password)
                || request.Username.Length > Limits.MaxIdentifierLength || request.Password.Length > Limits.MaxPasswordLength)
                return Results.BadRequest(new { error = "username and password are required" });

            var username = request.Username.Trim().ToLowerInvariant();
            var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.Username.ToLower() == username);

            // unknown username still checks a dummy hash, so a miss takes as long as a wrong password
            // and the response doesn't leak which usernames exist
            var passwordOk = PasswordHasher.Verify(admin?.PwHash ?? PasswordHasher.DummyHash, request.Password);
            if (admin is null || admin.DisabledAt is not null || !passwordOk)
                return Results.Json(new { error = ErrorMessages.InvalidCredentials }, statusCode: StatusCodes.Status401Unauthorized);

            // only the token's hash is stored, the token itself goes to the browser as a cookie
            var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var now = time.UtcNow();
            var session = new AdminSession
            {
                AdminId = admin.Id,
                TokenHash = TokenHasher.Hash(token),
                Ip = Normalize(http.Connection.RemoteIpAddress),
                ExpiresAt = now.AddHours(AdminSessionService.AbsoluteHours),
                IdleExpiresAt = now.AddMinutes(AdminSessionService.IdleMinutes),
            };
            db.AdminSessions.Add(session);
            await db.SaveChangesAsync();

            http.Response.Cookies.Append(AdminSessionService.CookieName, token, AdminSessionService.SessionCookieOptions(http.Request, session.ExpiresAt));
            return Results.NoContent();
        }
    }
}
