/*!************************************************************************
\file   GameServerRoutes.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines MapGameServerRoutes, which maps the /api/server
        routes that a game server calls. The server key check is on the
        whole group, so every route added here needs a valid key without
        anyone having to remember it.

        Called from Program.cs. Uses RequireServerKey from
        ServerKeyGuards.cs. The routes are not public through Caddy yet.
**************************************************************************/
using server.Auth;

namespace server.Endpoints.GameServer
{
    public static class GameServerRoutes
    {
        // only file that knows the urls and who can call them, one line per route. the key check goes on
        // the whole group, so a route added later can't be left open by mistake. these are for the game
        // server only. caddy makes /api/server/* public, since the game server calls in over the internet,
        // so this group-wide key check is the only thing protecting them
        public static void MapGameServerRoutes(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/server").RequireServerKey();

            group.MapGet("/ping", Ping.Handle);

            group.MapPost("/sessions/validate", ValidateSession.Handle);

            group.MapGet("/players/{id}/data", GetPlayerData.Handle);
            group.MapPost("/players/{id}/data", UpdatePlayerData.Handle);
        }
    }
}
