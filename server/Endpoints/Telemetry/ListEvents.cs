using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Endpoints.Telemetry
{
    public static class ListEvents
    {
        // get /api/events: every telemetry event, no paging yet
        public static async Task<IResult> Handle(Db db) =>
            Results.Ok(await db.TelemetryEvents.ToListAsync());
    }
}
