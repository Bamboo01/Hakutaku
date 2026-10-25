/*!************************************************************************
\file   PlayerRoutes.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines MapPlayerRoutes, which maps the admin's view of
        players (GET and POST /api/players) and puts the admin check on the
        whole group.

        Called from Program.cs. The public sign-in routes under the same
        path are in Endpoints/PlayerAuth.
**************************************************************************/
using server.Auth;

namespace server.Endpoints.Players
{
    public static class PlayerRoutes
    {
        // the admin's view of players. the public sign-in routes under /api/players/... are separate
        // (Endpoints/PlayerAuth/), and a player's own token doesn't unlock these yet
        public static void MapPlayerRoutes(this IEndpointRouteBuilder app)
        {
            // admin only, so the check goes on the whole group and not each route
            var group = app.MapGroup("/api/players").RequireAdmin();

            group.MapGet("", ListPlayers.Handle);
            group.MapPost("", CreatePlayer.Handle);
        }
    }
}
