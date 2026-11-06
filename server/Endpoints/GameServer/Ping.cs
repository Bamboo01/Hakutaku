/*!************************************************************************
\file   Ping.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the GET /api/server/ping route. It answers with the
        id of the server key that was used, which lets a game server check
        that its key works, and gives the tests a route to call that is
        protected by a server key.

        Mapped in GameServerRoutes.cs. The key is checked by
        RequireServerKeyFilter before the handler runs.
**************************************************************************/
using server.Auth;

namespace server.Endpoints.GameServer
{
    public static class Ping
    {
        // get /api/server/ping: needs a server key. the filter has already checked it and left its id on the request
        public static IResult Handle(HttpContext http) =>
            Results.Ok(new { ok = true, keyId = http.Items[ServerKeyGuards.ItemKey] });
    }
}
