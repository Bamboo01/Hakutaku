using server.Constants;
using server.Models;

namespace server.Endpoints.Admin
{
    public static class Logout
    {
        // POST /api/admin/logout: ends the current session and clears the cookie.
        public static async Task<IResult> Handle(Db db, HttpContext http)
        {
            var found = await AdminAuth.FindActiveSession(db, http);
            if (found is null)
                return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);

            found.Value.Session.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            http.Response.Cookies.Delete(AdminAuth.CookieName, new CookieOptions { Path = "/" });
            return Results.NoContent();
        }
    }
}
