/*!************************************************************************
\file   ListEvents.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the GET /api/events route, which returns every
        telemetry event as EventResponse records. There is no paging yet.

        Mapped in TelemetryRoutes.cs.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Endpoints.Telemetry
{
    public static class ListEvents
    {
        // get /api/events: every telemetry event, no paging yet
        public static async Task<IResult> Handle(Db db) =>
            Results.Ok(await db.TelemetryEvents
                .Select(e => new EventResponse(e.Id, e.PlayerId, e.EventType, e.Timestamp, e.Data))
                .ToListAsync());
    }
}
