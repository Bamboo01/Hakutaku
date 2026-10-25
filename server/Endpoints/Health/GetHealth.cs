/*!************************************************************************
\file   GetHealth.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the GET /Health route, which says the app is up
        without touching the database.

        Mapped in HealthRoutes.cs. The Jenkins smoke test calls it after
        every deploy, through Caddy.
**************************************************************************/
namespace server.Endpoints.Health
{
    public static class GetHealth
    {
        // get /Health: says the app is up, doesn't touch the db
        public static IResult Handle() => Results.Ok(new { health = "ok" });
    }
}
