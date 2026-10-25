/*!************************************************************************
\file   Me.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the GET /api/admin/me route. It says who the
        session cookie belongs to, which is how the admin UI finds out
        whether it is still logged in.

        Mapped in AdminRoutes.cs. Called by the web admin UI.
**************************************************************************/
using server.Auth;
using server.Constants;
using server.Models;

namespace server.Endpoints.Admin
{
    public static class Me
    {
        // get /api/admin/me: who the cookie belongs to. the admin ui calls it to see if it's still logged in
        public static async Task<IResult> Handle(AdminSessionService sessions, HttpContext http)
        {
            var found = await sessions.FindActiveSession(http);
            if (found is null)
                return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);

            return Results.Ok(new
            {
                username = found.Value.Admin.Username,
                email = found.Value.Admin.Email,
                role = found.Value.Admin.Role.ToName(),
            });
        }
    }
}
