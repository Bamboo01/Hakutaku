/*!************************************************************************
\file   GetStats.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   16th November 2026
\brief  This file defines the GET /api/admin/stats route, which gives the
        admin UI's Overview page its numbers: total players, and unique
        users, new users and logins for the last 24 hours and 30 days with
        the period before each, plus one entry per day for the charts.

        Mapped in AdminRoutes.cs with RequireAdmin, so any signed-in admin
        can read it. The counting is in StatsService.cs.
**************************************************************************/
using server.Services;

namespace server.Endpoints.Admin
{
    public static class GetStats
    {
        // get /api/admin/stats: answers StatsService.Stats as camelCase json
        public static async Task<IResult> Handle(StatsService stats) => Results.Ok(await stats.Read());
    }
}
