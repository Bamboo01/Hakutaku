using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;

namespace server.Endpoints.Admin
{
    public record LoginRequest(string? Username, string? Password);

    public static class Login
    {
        // POST /api/admin/login: checks the username and password, then sets the session cookie.
        public static async Task<IResult> Handle(Db db, HttpContext http, LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password)
                || request.Username.Length > Limits.MaxIdentifierLength || request.Password.Length > Limits.MaxPasswordLength)
                return Results.BadRequest(new { error = "username and password are required" });

            var username = request.Username.Trim().ToLowerInvariant();
            var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.Username.ToLower() == username);

            // An unknown username still checks a dummy hash, so a miss takes as long as a
            // wrong password and the response doesn't reveal which usernames exist.
            var passwordOk = AdminAuth.VerifyPassword(admin?.PwHash ?? AdminAuth.DummyHash, request.Password);
            if (admin is null || admin.DisabledAt is not null || !passwordOk)
                return Results.Json(new { error = ErrorMessages.InvalidCredentials }, statusCode: StatusCodes.Status401Unauthorized);

            // Only the hash of the token is stored; the token itself goes to the browser as a cookie.
            var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var now = DateTime.UtcNow;
            var session = new AdminSession
            {
                AdminId = admin.Id,
                TokenHash = AdminAuth.HashToken(token),
                Ip = AdminAuth.Normalize(http.Connection.RemoteIpAddress),
                ExpiresAt = now.AddHours(AdminAuth.AbsoluteHours),
                IdleExpiresAt = now.AddMinutes(AdminAuth.IdleMinutes),
            };
            db.AdminSessions.Add(session);
            await db.SaveChangesAsync();

            http.Response.Cookies.Append(AdminAuth.CookieName, token, AdminAuth.SessionCookieOptions(http.Request, session.ExpiresAt));
            return Results.NoContent();
        }
    }
}
