/*!************************************************************************
\file   HealthRoutes.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines MapHealthRoutes, which maps the public /Health
        route. The capital H matters because Caddy matches the path exactly.
        Never map / to an endpoint: static files skip requests that already
        matched an endpoint, so it would hide the Vue UI.

        Called from Program.cs. The handler is GetHealth.cs. Allowed through
        in caddy/Caddyfile.
**************************************************************************/
namespace server.Endpoints.Health
{
    public static class HealthRoutes
    {
        // public through caddy, the jenkins smoke test hits it after every deploy. capital H matters,
        // caddy matches the path exactly. never map "/" to an endpoint: static files skip any request that
        // already matched an endpoint, so it would hide the vue ui
        public static void MapHealthRoutes(this IEndpointRouteBuilder app)
        {
            app.MapGet("/Health", GetHealth.Handle);
        }
    }
}
