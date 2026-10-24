using server.Constants;
using server.Models;

namespace server.Endpoints.Admin
{
    public static class Me
    {
        // GET /api/admin/me: who the current cookie belongs to. The admin UI calls this to
        // find out whether it is still logged in.
        public static async Task<IResult> Handle(Db db, HttpContext http)
        {
            var found = await AdminAuth.FindActiveSession(db, http);
            if (found is null)
                return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);

            return Results.Ok(new
            {
                username = found.Value.Admin.Username,
                email = found.Value.Admin.Email,
                role = AdminAuth.RoleName(found.Value.Admin.Role),
            });
        }
    }
}
