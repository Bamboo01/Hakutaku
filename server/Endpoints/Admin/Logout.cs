using server.Auth;
using server.Constants;
using server.Hosting;
using server.Models;

namespace server.Endpoints.Admin
{
    public static class Logout
    {
        // post /api/admin/logout: ends the session and clears the cookie
        public static async Task<IResult> Handle(Db db, AdminSessionService sessions, TimeProvider time, HttpContext http)
        {
            var found = await sessions.FindActiveSession(http);
            if (found is null)
                return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);

            found.Value.Session.RevokedAt = time.UtcNow();
            await db.SaveChangesAsync();
            http.Response.Cookies.Delete(AdminSessionService.CookieName, new CookieOptions { Path = "/" });
            return Results.NoContent();
        }
    }
}
