/*!************************************************************************
\file   AdminRoutes.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines MapAdminRoutes, which maps the /api/admin routes
        (login, logout, me and the owner-only admin and server key
        management) to their handlers. It is the only file that knows these
        URLs and the login rate limit.

        Called from Program.cs. The handlers are the other files in this
        folder.
**************************************************************************/
using server.Constants;

namespace server.Endpoints.Admin
{
    public static class AdminRoutes
    {
        // only file that knows the urls and the rate limit, one line per route. no check on the group
        // because login has to stay open. the others check the session themselves, and the /admins and
        // /server-keys routes also need the owner
        public static void MapAdminRoutes(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/admin");

            group.MapPost("/login", Login.Handle).RequireRateLimiting(RateLimits.AdminLogin);
            group.MapPost("/logout", Logout.Handle);
            group.MapGet("/me", Me.Handle);

            group.MapGet("/admins", ListAdmins.Handle);
            group.MapPost("/admins", CreateAdmin.Handle);
            group.MapDelete("/admins/{id}", DeactivateAdmin.Handle);

            group.MapGet("/server-keys", ListServerKeys.Handle);
            group.MapPost("/server-keys", CreateServerKey.Handle);
            group.MapDelete("/server-keys/{id}", RevokeServerKey.Handle);
        }
    }
}
