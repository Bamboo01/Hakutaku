/*!************************************************************************
\file   TelemetryRoutes.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines MapTelemetryRoutes, which maps GET and POST
        /api/events and puts the admin check on the whole group. It is the
        only file that knows these URLs and who can call them.

        Called from Program.cs. Handlers are ListEvents.cs and
        CreateEvent.cs. Uses RequireAdmin from AdminGuards.cs.
**************************************************************************/
using server.Auth;

namespace server.Endpoints.Telemetry
{
    public static class TelemetryRoutes
    {
        // only file that knows the urls and who can call them, one line per route
        public static void MapTelemetryRoutes(this IEndpointRouteBuilder app)
        {
            // admin only for now, so the check goes on the whole group and not each route
            var group = app.MapGroup("/api/events").RequireAdmin();

            group.MapGet("", ListEvents.Handle);
            group.MapPost("", CreateEvent.Handle);
        }
    }
}
